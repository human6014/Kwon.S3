using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine.Networking;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>버킷에 올라가 있는 객체 하나의 요약.</summary>
    public struct AwsObjectInfo
    {
        public string Key;
        public DateTime LastModifiedUtc;
        public long Size;

        /// <summary>따옴표를 벗긴 ETag. 단일 PUT 으로 올린 객체라면 본문 MD5 와 같다.</summary>
        public string ETag;
    }

    /// <summary>AWS 호출 결과. 실패해도 예외를 던지지 않고 사유를 담아 돌려준다.</summary>
    public readonly struct AwsResult<T>
    {
        private AwsResult(bool isSuccess, T value, string error)
        {
            IsSuccess = isSuccess;
            Value = value;
            Error = error;
        }

        public bool IsSuccess { get; }

        public T Value { get; }

        public string Error { get; }

        public static AwsResult<T> Ok(T value) => new AwsResult<T>(true, value, null);

        public static AwsResult<T> Fail(string error) => new AwsResult<T>(false, default, error);
    }

    /// <summary>
    /// S3 를 직접 호출한다. 필요한 동작이 올리기·받기·목록 셋뿐이라 SDK 없이 <see cref="AwsSigV4"/>로 서명한다.
    /// 에디터에서는 플레이어 루프가 돌지 않아 UniTask 기본 대기가 진행되지 않으므로,
    /// <see cref="S3Downloader"/>와 같은 방식으로 <see cref="EditorApplication.update"/>에서 완료를 감시한다.
    /// </summary>
    public static class AwsS3Client
    {
        private const string Service = "s3";
        private const int TimeoutSeconds = 60;

        /// <summary>객체 하나를 올린다.</summary>
        public static async UniTask<AwsResult<string>> PutObjectAsync(
            string bucket, string region, string key, byte[] body, string contentType, string cacheControl)
        {
            var request = new AwsRequest("PUT", BuildHost(bucket, region), "/" + key.TrimStart('/'), body);
            request.Headers["Content-Type"] = contentType;
            if (!string.IsNullOrEmpty(cacheControl)) request.Headers["Cache-Control"] = cacheControl;

            var response = await SendAsync(request, region);
            if (!response.IsSuccess) return AwsResult<string>.Fail(response.Error);

            return AwsResult<string>.Ok(key);
        }

        /// <summary>객체 하나를 문자열로 받는다.</summary>
        public static async UniTask<AwsResult<string>> GetObjectAsync(string bucket, string region, string key)
        {
            var request = new AwsRequest("GET", BuildHost(bucket, region), "/" + key.TrimStart('/'));

            var response = await SendAsync(request, region);
            if (!response.IsSuccess) return AwsResult<string>.Fail(response.Error);

            return AwsResult<string>.Ok(response.Text);
        }

        /// <summary>
        /// 객체 하나를 지운다. S3 는 없는 키를 지워도 204 로 성공 처리하므로, 이 호출의 성공이 "있었다"를 뜻하지는 않는다.
        /// </summary>
        public static async UniTask<AwsResult<string>> DeleteObjectAsync(string bucket, string region, string key)
        {
            var request = new AwsRequest("DELETE", BuildHost(bucket, region), "/" + key.TrimStart('/'));

            var response = await SendAsync(request, region);
            if (!response.IsSuccess) return AwsResult<string>.Fail(response.Error);

            return AwsResult<string>.Ok(key);
        }

        /// <summary>프리픽스 아래 객체를 모두 나열한다. 1000개를 넘으면 이어서 받아온다.</summary>
        public static async UniTask<AwsResult<List<AwsObjectInfo>>> ListObjectsAsync(
            string bucket, string region, string prefix)
        {
            var results = new List<AwsObjectInfo>();
            string continuationToken = null;

            do
            {
                var request = new AwsRequest("GET", BuildHost(bucket, region), "/");
                request.Query.Add(new KeyValuePair<string, string>("list-type", "2"));
                if (!string.IsNullOrEmpty(prefix))
                {
                    request.Query.Add(new KeyValuePair<string, string>("prefix", prefix));
                }

                if (continuationToken != null)
                {
                    request.Query.Add(new KeyValuePair<string, string>("continuation-token", continuationToken));
                }

                var response = await SendAsync(request, region);
                if (!response.IsSuccess) return AwsResult<List<AwsObjectInfo>>.Fail(response.Error);

                try
                {
                    continuationToken = ParseListResponse(response.Text, results);
                }
                catch (Exception e)
                {
                    return AwsResult<List<AwsObjectInfo>>.Fail($"목록 응답을 해석하지 못했습니다: {e.Message}");
                }
            }
            while (continuationToken != null);

            return AwsResult<List<AwsObjectInfo>>.Ok(results);
        }

        /// <summary>가상 호스팅 방식 엔드포인트. 버킷 이름에 점이 있으면 인증서가 맞지 않으므로 쓰지 않는다.</summary>
        private static string BuildHost(string bucket, string region) => $"{bucket}.s3.{region}.amazonaws.com";

        /// <returns>이어받을 토큰. 더 없으면 null.</returns>
        private static string ParseListResponse(string xml, List<AwsObjectInfo> results)
        {
            var document = XDocument.Parse(xml);
            var root = document.Root;
            if (root == null) return null;

            var ns = root.Name.Namespace;

            foreach (var element in root.Elements(ns + "Contents"))
            {
                var info = new AwsObjectInfo
                {
                    Key = (string)element.Element(ns + "Key"),
                    ETag = ((string)element.Element(ns + "ETag") ?? string.Empty).Trim('"'),
                };

                if (long.TryParse((string)element.Element(ns + "Size"), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var size))
                {
                    info.Size = size;
                }

                if (DateTime.TryParse((string)element.Element(ns + "LastModified"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var modified))
                {
                    info.LastModifiedUtc = modified;
                }

                results.Add(info);
            }

            var isTruncated = (string)root.Element(ns + "IsTruncated");
            if (!string.Equals(isTruncated, "true", StringComparison.OrdinalIgnoreCase)) return null;

            return (string)root.Element(ns + "NextContinuationToken");
        }

        private static UniTask<AwsResponse> SendAsync(AwsRequest request, string region)
        {
            AwsSigV4.Sign(request, AwsCredentials.AccessKeyId, AwsCredentials.SecretAccessKey,
                region, Service, DateTime.UtcNow);

            var url = request.Url;
            var webRequest = new UnityWebRequest(url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };

            if (request.Payload.Length > 0)
            {
                webRequest.uploadHandler = new UploadHandlerRaw(request.Payload);
            }

            foreach (var header in request.Headers)
            {
                // host 는 UnityWebRequest 가 URL 에서 직접 채운다. 서명에는 들어가지만 여기서 세팅하면 거부된다.
                if (string.Equals(header.Key, "host", StringComparison.OrdinalIgnoreCase)) continue;
                webRequest.SetRequestHeader(header.Key, header.Value);
            }

            var source = new UniTaskCompletionSource<AwsResponse>();
            var operation = webRequest.SendWebRequest();

            void OnEditorUpdate()
            {
                if (!operation.isDone) return;

                EditorApplication.update -= OnEditorUpdate;

                var text = webRequest.downloadHandler?.text ?? string.Empty;
                var statusCode = webRequest.responseCode;
                var failed = webRequest.result != UnityWebRequest.Result.Success;
                var error = failed
                    ? $"{request.Method} {url}\nHTTP {statusCode} {webRequest.error}\n{Describe(text)}"
                    : null;

                webRequest.Dispose();

                source.TrySetResult(new AwsResponse(!failed, statusCode, text, error));
            }

            EditorApplication.update += OnEditorUpdate;

            return source.Task;
        }

        /// <summary>S3 가 돌려준 XML 오류 본문에서 사람이 볼 부분만 뽑는다. 서명 디버깅은 이 문자열이 전부다.</summary>
        private static string Describe(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(본문 없음)";

            try
            {
                var root = XDocument.Parse(body).Root;
                if (root != null)
                {
                    var ns = root.Name.Namespace;
                    var code = (string)root.Element(ns + "Code");
                    var message = (string)root.Element(ns + "Message");

                    if (!string.IsNullOrEmpty(code))
                    {
                        return $"{code}: {message}";
                    }
                }
            }
            catch (Exception)
            {
                // XML 이 아니면 그냥 본문 앞부분을 보여준다.
            }

            return body.Length <= 300 ? body : body.Substring(0, 300) + "...";
        }

        private readonly struct AwsResponse
        {
            public AwsResponse(bool isSuccess, long statusCode, string text, string error)
            {
                IsSuccess = isSuccess;
                StatusCode = statusCode;
                Text = text;
                Error = error;
            }

            public bool IsSuccess { get; }

            public long StatusCode { get; }

            public string Text { get; }

            public string Error { get; }
        }
    }
}
