using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 임포트할 때 남겨둔 CSV 원본으로 배포 번들과 같은 형식의 파일을 만든다.
    /// 에디터 오프라인 모드가 CloudFront 대신 이걸 읽는다.
    /// </summary>
    /// <remarks>
    /// 배포용(<see cref="S3CdnPublisher"/>)과 달리 <b>시트를 받지 않는다.</b> 목적이 네트워크를 안 타는 것이라
    /// 이미 받아둔 <c>Library/S3Cache</c> 로만 만든다. 그래서 내용은 <b>마지막 임포트 시점</b>의 것이고,
    /// 임포트가 끝날 때마다 자동으로 다시 만들어져 저절로 최신이 된다.
    /// </remarks>
    public static class S3OfflineBundle
    {
        /// <summary>임포터 창의 [오프라인 번들 다시 만들기] 버튼이 부른다.</summary>
        public static void RebuildAndLog()
        {
            var count = Rebuild(S3ImportSettings.GetOrCreate());

            if (count > 0)
            {
                Debug.Log($"[S3] 오프라인 번들을 만들었습니다. 테이블 {count}개 → {S3Config.OfflineBundlePath}");
            }
        }

        /// <summary>캐시에 있는 시트로 번들 파일을 새로 쓴다.</summary>
        /// <returns>담긴 테이블 수. 재료가 없으면 0.</returns>
        public static int Rebuild(S3ImportSettings settings)
        {
            if (settings == null) return 0;

            var entries = new System.Collections.Generic.List<S3BundleEntry>();
            var missing = new System.Collections.Generic.List<string>();

            foreach (var entry in settings.sheets)
            {
                if (entry == null || !entry.enabled) continue;
                if (string.IsNullOrWhiteSpace(entry.tableName)) continue;

                var csv = S3Paths.ReadCache(entry.tableName);
                if (csv == null)
                {
                    missing.Add(entry.tableName);
                    continue;
                }

                entries.Add(new S3BundleEntry
                {
                    name = S3CodeGenerator.TableClassName(entry.tableName),
                    headerRow = entry.layout.HeaderRowNumber,
                    startColumn = entry.layout.StartColumnName,
                    csv = StripBom(csv),
                });
            }

            if (missing.Count > 0)
            {
                // 빠진 테이블이 있으면 오프라인 실행이 그 테이블에서 막힌다. 조용히 넘기면 원인을 못 찾는다.
                Debug.LogWarning(
                    $"[S3] 캐시에 없는 시트 {missing.Count}개는 오프라인 번들에서 빠집니다: {string.Join(", ", missing)}\n" +
                    "'전체 임포트' 를 한 번 돌리면 채워집니다.");
            }

            if (entries.Count == 0) return 0;

            var bundle = new S3Bundle
            {
                version = S3Bundle.CurrentVersion,
                tables = entries.ToArray(),
            };

            var path = S3Config.OfflineBundlePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            File.WriteAllText(path, JsonUtility.ToJson(bundle), new UTF8Encoding(false));

            return entries.Count;
        }

        private static string StripBom(string text)
        {
            const char ByteOrderMark = (char)0xFEFF;

            if (!string.IsNullOrEmpty(text) && text[0] == ByteOrderMark) return text.Substring(1);

            return text ?? string.Empty;
        }
    }
}
