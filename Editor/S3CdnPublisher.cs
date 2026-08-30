using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>테이블 하나가 버킷에서 어떤 상태인지.</summary>
    public struct S3PublishStatus
    {
        /// <summary>테이블 에셋 이름이자 원격 키의 파일명. 예: MonsterDataTable</summary>
        public string TableName;

        public string Key;

        /// <summary>버킷에 객체가 있는지.</summary>
        public bool ExistsRemote;

        /// <summary>시트를 받아 비교했는지. 고아 키는 비교할 시트가 없어 false 다.</summary>
        public bool ComparedWithSheet;

        /// <summary>시트 내용과 버킷 내용이 같은지.</summary>
        public bool MatchesSheet;

        /// <summary>임포터 설정에 없는데 버킷에만 있는 키. 삭제 대상 후보다.</summary>
        public bool IsOrphan;

        public DateTime LastModifiedUtc;

        public long Size;

        public string Note;

        public string Summary
        {
            get
            {
                if (IsOrphan) return "? 시트에 없는 키";
                if (!ExistsRemote) return "✗ 버킷에 없음";
                if (!ComparedWithSheet) return "· 대조 안 함";
                return MatchesSheet ? "✓ 일치" : "⚠ 시트와 다름";
            }
        }
    }

    /// <summary>업로드 한 번의 결과 요약.</summary>
    public struct S3PublishSummary
    {
        public int Succeeded;
        public int Failed;
        public bool WasCancelled;
    }

    /// <summary>
    /// 시트 내용을 AWS 버킷으로 올리고, 올라간 상태를 되읽어 확인한다.
    /// <para>
    /// 캐시(<c>Library/S3Cache</c>)를 재활용하지 않고 시트에서 다시 받는다. 그 캐시의 파일명은 탭 이름(Monster.csv)이라
    /// 원격 키(MonsterDataTable.csv)와 다르고, 마지막 임포트에 포함된 탭만 들어 있어 조용히 빠지기 때문이다.
    /// </para>
    /// </summary>
    public static class S3CdnPublisher
    {
        private const string ContentType = "application/json; charset=utf-8";

        /// <summary>짧게 잡아 시트 수정이 1분 안에 게임에 닿게 한다. 무효화를 따로 걸지 않아도 되는 이유다.</summary>
        private const string CacheControl = "max-age=60";

        /// <summary>테이블 전부가 들어가는 단 하나의 키.</summary>
        public static string BundleKey => $"{S3Config.RemotePrefix}/{S3Config.BundleFileName}";

        /// <summary>
        /// 활성화된 시트를 전부 받아 <b>번들 하나로 묶어</b> 올린다.
        /// 예전 키는 지우지 않는다 — 이미 배포된 빌드가 한동안 그 키를 계속 읽기 때문이다.
        /// </summary>
        public static async UniTask<S3PublishSummary> PublishAsync(S3ImportSettings settings, S3UploadTarget target)
        {
            var summary = new S3PublishSummary();

            var invalid = AwsCredentials.Validate(target);
            if (invalid != null)
            {
                Debug.LogError($"[S3] 업로드 전에 AWS 설정이 필요합니다. {invalid} (TD/S3/AWS 설정)");
                summary.Failed = 1;
                return summary;
            }

            if (target == S3UploadTarget.Live && !ConfirmLive("업로드"))
            {
                summary.WasCancelled = true;
                return summary;
            }

            var (json, cancelled) = await BuildBundleJsonAsync(settings);

            if (cancelled)
            {
                summary.WasCancelled = true;
                return summary;
            }

            if (json == null)
            {
                summary.Failed = 1;
                return summary;
            }

            var body = ToUtf8(json);
            var result = await AwsS3Client.PutObjectAsync(
                AwsCredentials.BucketFor(target), AwsCredentials.Region, BundleKey, body, ContentType, CacheControl);

            if (result.IsSuccess)
            {
                summary.Succeeded = 1;
                Debug.Log($"[S3] {TargetLabel(target)} 업로드 완료. {BundleKey} ({body.Length:N0} bytes)");
            }
            else
            {
                summary.Failed = 1;
                Debug.LogError($"[S3] {BundleKey} 업로드 실패\n{result.Error}");
            }

            return summary;
        }

        /// <summary>
        /// 시트를 전부 받아 번들 JSON 을 만든다.
        /// <para>
        /// <b>내용이 같으면 바이트도 같아야 한다.</b> 상태 보기가 ETag 와 이 결과의 MD5 를 맞춰보기 때문이다.
        /// 그래서 시각 같은 값을 넣지 않고, 항목 순서도 임포터 설정 순서 그대로 둔다.
        /// </para>
        /// </summary>
        /// <returns>실패하면 json 이 null. 사용자가 중단하면 cancelled 가 true.</returns>
        private static async UniTask<(string json, bool cancelled)> BuildBundleJsonAsync(S3ImportSettings settings)
        {
            var entries = CollectEntries(settings);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[S3] 올릴 시트가 없습니다. 임포터 설정에서 테이블 이름과 활성화 여부를 확인하세요.");
                return (null, false);
            }

            var bundle = new S3Bundle
            {
                version = S3Bundle.CurrentVersion,
                tables = new S3BundleEntry[entries.Count],
            };

            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var tableName = S3CodeGenerator.TableClassName(entry.tableName);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "시트 받는 중", $"{tableName} ({i + 1}/{entries.Count})", (float)i / entries.Count))
                    {
                        return (null, true);
                    }

                    try
                    {
                        var csv = await S3Downloader.GetTextAsync(
                            S3Config.CsvUrl(entry.NormalizedDocumentId, entry.NormalizedGid));

                        // 읽기 좌표를 함께 싣는다. 예전엔 테이블 에셋에 구워뒀지만 이제 에셋이 없으므로
                        // 시트 설정에서 번들로 곧장 흐른다.
                        bundle.tables[i] = new S3BundleEntry
                        {
                            name = tableName,
                            headerRow = entry.layout.HeaderRowNumber,
                            startColumn = entry.layout.StartColumnName,
                            csv = StripBom(csv),
                        };
                    }
                    catch (Exception e)
                    {
                        // 한 장이라도 빠지면 그 테이블을 읽는 빌드가 실행되지 않는다. 반쪽 번들을 올리지 않는다.
                        Debug.LogError($"[S3] {tableName} 시트를 받지 못해 업로드를 중단합니다: {e.Message}");
                        return (null, false);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return (JsonUtility.ToJson(bundle), false);
        }

        /// <summary>
        /// 버킷 목록을 받아 시트 내용과 대조한다.
        /// 비교는 ETag(단일 PUT 이면 본문 MD5)와 시트 CSV 의 MD5 를 맞춰본다.
        /// </summary>
        public static async UniTask<List<S3PublishStatus>> InspectAsync(
            S3ImportSettings settings, S3UploadTarget target, bool compareWithSheets = true)
        {
            var statuses = new List<S3PublishStatus>();

            var invalid = AwsCredentials.Validate(target);
            if (invalid != null)
            {
                Debug.LogError($"[S3] 상태를 보려면 AWS 설정이 필요합니다. {invalid} (TD/S3/AWS 설정)");
                return statuses;
            }

            var bucket = AwsCredentials.BucketFor(target);
            var region = AwsCredentials.Region;

            var listed = await AwsS3Client.ListObjectsAsync(bucket, region, S3Config.RemotePrefix);
            if (!listed.IsSuccess)
            {
                Debug.LogError($"[S3] 목록 조회 실패\n{listed.Error}");
                return statuses;
            }

            var remoteByKey = new Dictionary<string, AwsObjectInfo>(StringComparer.Ordinal);
            foreach (var info in listed.Value) remoteByKey[info.Key] = info;

            var bundleStatus = new S3PublishStatus
            {
                TableName = S3Config.BundleFileName,
                Key = BundleKey,
            };

            if (remoteByKey.TryGetValue(BundleKey, out var bundleInfo))
            {
                bundleStatus.ExistsRemote = true;
                bundleStatus.LastModifiedUtc = bundleInfo.LastModifiedUtc;
                bundleStatus.Size = bundleInfo.Size;
            }
            else
            {
                bundleStatus.Note = "아직 올린 적이 없습니다. [AWS로 업로드] 를 누르세요.";
            }

            if (compareWithSheets && bundleStatus.ExistsRemote)
            {
                // 시트에서 지금 만든 번들과 올라가 있는 번들을 바이트로 맞춰본다.
                var (json, cancelled) = await BuildBundleJsonAsync(settings);

                if (json != null)
                {
                    bundleStatus.ComparedWithSheet = true;
                    bundleStatus.MatchesSheet = string.Equals(
                        ToMd5Hex(ToUtf8(json)), bundleInfo.ETag, StringComparison.OrdinalIgnoreCase);
                }
                else if (!cancelled)
                {
                    bundleStatus.Note = "시트를 받지 못해 내용을 대조하지 못했습니다.";
                }
            }

            statuses.Add(bundleStatus);

            // 번들 말고 프리픽스 아래 남아 있는 키. 옛 형식의 테이블별 CSV 나 이름이 바뀐 흔적이다.
            foreach (var pair in remoteByKey)
            {
                if (string.Equals(pair.Key, BundleKey, StringComparison.Ordinal)) continue;

                statuses.Add(new S3PublishStatus
                {
                    TableName = System.IO.Path.GetFileNameWithoutExtension(pair.Key),
                    Key = pair.Key,
                    ExistsRemote = true,
                    IsOrphan = true,
                    LastModifiedUtc = pair.Value.LastModifiedUtc,
                    Size = pair.Value.Size,
                    Note = "번들이 아닌 키입니다. 이 키를 읽는 배포본이 남아 있으면 실행되지 않습니다.",
                });
            }

            return statuses;
        }

        /// <summary>
        /// 시트에 없는 고아 키를 지운다. 시트에서 탭을 지웠거나 이름을 바꾼 뒤 남은 것들이다.
        /// <para>
        /// <b>업로드는 이 일을 자동으로 하지 않는다.</b> 폴백이 없어진 뒤로, 아직 그 키를 읽는 배포본이 남아 있으면
        /// 데이터가 낡는 정도가 아니라 <b>실행 자체가 되지 않는다.</b> 그래서 지울 대상을 눈으로 확인하고 누르게 한다.
        /// </para>
        /// </summary>
        public static async UniTask<int> DeleteOrphansAsync(
            S3UploadTarget target, IReadOnlyList<string> keys)
        {
            if (keys == null || keys.Count == 0) return 0;

            var invalid = AwsCredentials.Validate(target);
            if (invalid != null)
            {
                Debug.LogError($"[S3] 삭제 전에 AWS 설정이 필요합니다. {invalid} (TD/S3/AWS 설정)");
                return 0;
            }

            var listed = string.Join("\n", keys);
            var proceed = EditorUtility.DisplayDialog(
                $"{TargetLabel(target)} 버킷에서 삭제",
                $"아래 {keys.Count}개를 지웁니다.\n\n{listed}\n\n" +
                "이 키를 읽고 있는 배포본이 남아 있으면 그 빌드는 실행되지 않습니다.\n" +
                "보통은 1~2 릴리즈 지난 뒤에 지웁니다.",
                "지운다", "취소");

            if (!proceed) return 0;

            // 라이브는 한 번 더 묻는다. 실수로 운영 데이터를 지우는 걸 막는 마지막 지점이다.
            if (target == S3UploadTarget.Live && !ConfirmLive("삭제")) return 0;

            var bucket = AwsCredentials.BucketFor(target);
            var region = AwsCredentials.Region;
            var deleted = 0;

            try
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        $"AWS 삭제 ({TargetLabel(target)})", keys[i], (float)i / keys.Count);

                    var result = await AwsS3Client.DeleteObjectAsync(bucket, region, keys[i]);

                    if (result.IsSuccess)
                    {
                        deleted++;
                        Debug.Log($"[S3] 삭제 {keys[i]}");
                    }
                    else
                    {
                        Debug.LogError($"[S3] {keys[i]} 삭제 실패\n{result.Error}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[S3] {TargetLabel(target)} 삭제 완료. {deleted}/{keys.Count}");

            return deleted;
        }

        public static string TargetLabel(S3UploadTarget target) => target == S3UploadTarget.Live ? "라이브" : "테섭";

        /// <summary>라이브에 실수로 쏘는 걸 막는 유일한 방어선이다.</summary>
        public static bool ConfirmLive(string action) =>
            EditorUtility.DisplayDialog(
                "라이브 배포 확인",
                $"라이브 버킷({AwsCredentials.LiveBucket})에 {action}합니다.\n실제 서비스 데이터가 바뀝니다. 계속할까요?",
                "계속", "취소");

        private static List<S3SheetEntry> CollectEntries(S3ImportSettings settings)
        {
            var entries = new List<S3SheetEntry>();
            if (settings == null) return entries;

            foreach (var entry in settings.sheets)
            {
                if (entry == null || !entry.enabled) continue;
                if (string.IsNullOrWhiteSpace(entry.tableName)) continue;

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// 구글이 붙여 보내는 BOM 을 뗀다. 번들에 그대로 넣으면 CSV 첫 컬럼 이름 앞에 눈에 안 보이는 글자가 붙는다.
        /// </summary>
        private static string StripBom(string text)
        {
            const char ByteOrderMark = (char)0xFEFF;

            if (!string.IsNullOrEmpty(text) && text[0] == ByteOrderMark) return text.Substring(1);

            return text ?? string.Empty;
        }

        /// <summary>BOM 없는 UTF-8 바이트.</summary>
        private static byte[] ToUtf8(string text) => new UTF8Encoding(false).GetBytes(StripBom(text));

        private static string ToMd5Hex(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(data);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }
}
