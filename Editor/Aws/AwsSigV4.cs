using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// AWS 요청 하나를 서명 직전 상태로 들고 있는 그릇.
    /// <see cref="Path"/>와 질의 값은 <b>인코딩 전 원본</b>을 넣는다. 인코딩은 서명과 URL 조립이 같은 규칙으로 처리한다.
    /// 둘을 따로 인코딩하면 서명과 실제 전송이 어긋나 SignatureDoesNotMatch 가 난다.
    /// </summary>
    public sealed class AwsRequest
    {
        public AwsRequest(string method, string host, string path, byte[] payload = null)
        {
            Method = method;
            Host = host;
            Path = string.IsNullOrEmpty(path) ? "/" : path;
            Payload = payload ?? Array.Empty<byte>();
        }

        public string Method { get; }

        public string Host { get; }

        /// <summary>"/config/v1/MonsterDataTable.csv" 처럼 슬래시로 시작하는 원본 경로.</summary>
        public string Path { get; }

        public byte[] Payload { get; }

        /// <summary>질의 문자열. 순서는 신경 쓰지 않아도 된다. 서명이 알아서 정렬한다.</summary>
        public List<KeyValuePair<string, string>> Query { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>보낼 헤더. host 와 x-amz-* 는 <see cref="AwsSigV4.Sign"/>이 채우므로 직접 넣지 않는다.</summary>
        public Dictionary<string, string> Headers { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Url
        {
            get
            {
                var builder = new StringBuilder("https://").Append(Host).Append(AwsSigV4.EncodePath(Path));
                var query = AwsSigV4.BuildCanonicalQuery(Query);
                if (query.Length > 0) builder.Append('?').Append(query);
                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// AWS Signature Version 4 서명. 에디터 전용이라 SDK 없이 직접 계산한다.
    /// 여기서 만드는 헤더 세 개(Authorization, x-amz-date, x-amz-content-sha256)를 그대로 요청에 실으면 된다.
    /// </summary>
    public static class AwsSigV4
    {
        public const string Algorithm = "AWS4-HMAC-SHA256";

        private const string Terminator = "aws4_request";
        private const string UnreservedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~";

        /// <summary>빈 본문의 SHA256. GET·목록 조회에서 매번 쓰인다.</summary>
        public static string EmptyPayloadHash => ToHex(Sha256(Array.Empty<byte>()));

        /// <summary>
        /// 요청에 서명 헤더를 채운다. 호출 후 <paramref name="request"/>.Headers 를 그대로 전송하면 된다.
        /// </summary>
        public static void Sign(
            AwsRequest request,
            string accessKeyId,
            string secretAccessKey,
            string region,
            string service,
            DateTime utcNow)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var amzDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var payloadHash = ToHex(Sha256(request.Payload));

            // S3 는 이 두 헤더를 반드시 서명에 포함해야 한다.
            request.Headers["x-amz-date"] = amzDate;
            request.Headers["x-amz-content-sha256"] = payloadHash;

            // host 는 UnityWebRequest 가 직접 세팅할 수 없지만, 실제 전송에서 URL 의 호스트가 그대로 나가므로
            // 정규 헤더에는 넣어야 서버 계산과 일치한다. 전송 목록에서는 다시 빼낸다.
            var signedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = request.Host,
            };

            foreach (var header in request.Headers)
            {
                signedHeaders[header.Key.ToLowerInvariant()] = (header.Value ?? string.Empty).Trim();
            }

            var canonicalHeaders = new StringBuilder();
            var signedHeaderNames = new StringBuilder();

            foreach (var header in signedHeaders)
            {
                canonicalHeaders.Append(header.Key).Append(':').Append(header.Value).Append('\n');
                if (signedHeaderNames.Length > 0) signedHeaderNames.Append(';');
                signedHeaderNames.Append(header.Key);
            }

            var canonicalRequest = string.Join("\n",
                request.Method,
                EncodePath(request.Path),
                BuildCanonicalQuery(request.Query),
                canonicalHeaders.ToString(),
                signedHeaderNames.ToString(),
                payloadHash);

            var scope = $"{dateStamp}/{region}/{service}/{Terminator}";
            var stringToSign = string.Join("\n",
                Algorithm,
                amzDate,
                scope,
                ToHex(Sha256(Encoding.UTF8.GetBytes(canonicalRequest))));

            var signingKey = DeriveSigningKey(secretAccessKey, dateStamp, region, service);
            var signature = ToHex(HmacSha256(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

            request.Headers["Authorization"] =
                $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaderNames}, Signature={signature}";
        }

        /// <summary>경로를 RFC3986 으로 인코딩하되 구분자 슬래시는 남긴다.</summary>
        public static string EncodePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";

            var builder = new StringBuilder();
            var segments = path.Split('/');

            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0) builder.Append('/');
                builder.Append(Encode(segments[i]));
            }

            return builder.ToString();
        }

        /// <summary>정규 질의 문자열. 키 기준 정렬 후 키·값 모두 인코딩한다.</summary>
        public static string BuildCanonicalQuery(IReadOnlyList<KeyValuePair<string, string>> query)
        {
            if (query == null || query.Count == 0) return string.Empty;

            var encoded = new List<string>(query.Count);
            foreach (var pair in query)
            {
                encoded.Add($"{Encode(pair.Key)}={Encode(pair.Value ?? string.Empty)}");
            }

            encoded.Sort(StringComparer.Ordinal);
            return string.Join("&", encoded);
        }

        /// <summary>
        /// RFC3986 인코딩. <see cref="Uri.EscapeDataString"/>은 런타임 버전에 따라 남기는 문자가 달라
        /// 서명이 어긋날 수 있으므로 직접 만든다.
        /// </summary>
        public static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length * 2);

            foreach (var b in Encoding.UTF8.GetBytes(value))
            {
                var c = (char)b;
                if (UnreservedChars.IndexOf(c) >= 0) builder.Append(c);
                else builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static byte[] DeriveSigningKey(string secretAccessKey, string dateStamp, string region, string service)
        {
            var key = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
            key = HmacSha256(key, Encoding.UTF8.GetBytes(dateStamp));
            key = HmacSha256(key, Encoding.UTF8.GetBytes(region));
            key = HmacSha256(key, Encoding.UTF8.GetBytes(service));
            return HmacSha256(key, Encoding.UTF8.GetBytes(Terminator));
        }

        private static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        private static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        private static string ToHex(byte[] data)
        {
            var builder = new StringBuilder(data.Length * 2);
            foreach (var b in data) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
