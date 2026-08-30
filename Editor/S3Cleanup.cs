using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 더 이상 아무 시트도 쓰지 않는 산출물을 찾아 지운다.
    /// 시트의 고유키는 gid이므로 테이블 이름은 언제든 바뀔 수 있고,
    /// 그때마다 예전 이름으로 만들어진 스크립트와 에셋이 남는다.
    /// </summary>
    public static class S3Cleanup
    {
        /// <summary>테이블 하나가 만들어내는 파일 경로들.</summary>
        public static List<string> ArtifactPaths(S3ImportSettings settings, string tableName)
        {
            return new List<string>
            {
                $"{settings.scriptOutputFolder}/{S3CodeGenerator.DataClassName(tableName)}.cs",
                $"{settings.scriptOutputFolder}/{S3CodeGenerator.TableClassName(tableName)}.cs",
            };
        }

        /// <summary>해당 테이블 이름으로 만들어진 파일을 모두 지운다. 지운 경로를 돌려준다.</summary>
        public static List<string> DeleteArtifacts(S3ImportSettings settings, string tableName)
        {
            var deleted = new List<string>();
            if (string.IsNullOrWhiteSpace(tableName)) return deleted;

            foreach (var path in ArtifactPaths(settings, tableName))
            {
                if (!Exists(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) deleted.Add(path);
            }

            // 다운로드 캐시도 테이블 이름으로 저장되므로 같이 지운다.
            var cache = S3Paths.CacheFilePath(tableName);
            if (File.Exists(cache)) File.Delete(cache);

            return deleted;
        }

        /// <summary>
        /// 출력 폴더에 있지만 지금 설정의 어떤 시트에도 속하지 않는 파일을 찾는다.
        /// 시트 항목을 통째로 지웠을 때 남는 파일이 여기 걸린다.
        /// </summary>
        public static List<string> FindOrphans(S3ImportSettings settings)
        {
            var owned = new HashSet<string>();
            foreach (var entry in settings.sheets)
            {
                if (string.IsNullOrWhiteSpace(entry.tableName)) continue;

                foreach (var path in ArtifactPaths(settings, entry.tableName)) owned.Add(path);
            }

            var orphans = new List<string>();

            // 생성 스크립트: {이름}Data.cs 와 {이름}DataTable.cs 만 관리 대상이다.
            // 레지스트리는 테이블 하나에 딸린 산출물이 아니라 매 임포트마다 통째로 다시 쓰이므로 대상이 아니다.
            CollectOrphans(settings.scriptOutputFolder, "*.cs", owned, orphans,
                name => name != S3CodeGenerator.RegistryClassName &&
                        (name.EndsWith("Data") || name.EndsWith("DataTable")));

            // enum 은 이름으로 등록돼 있어 다운로드 없이도 주인 없는 파일을 알 수 있다.
            orphans.AddRange(S3EnumGenerator.FindOrphans(settings));

            return orphans;
        }

        /// <summary>찾은 고아 파일을 지운다. 지운 경로를 돌려준다.</summary>
        public static List<string> DeleteOrphans(S3ImportSettings settings)
        {
            var deleted = new List<string>();

            foreach (var path in FindOrphans(settings))
            {
                if (AssetDatabase.DeleteAsset(path)) deleted.Add(path);
            }

            return deleted;
        }

        private static void CollectOrphans(
            string folder, string pattern, HashSet<string> owned, List<string> orphans, System.Func<string, bool> isManaged)
        {
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!isManaged(name)) continue;

                var assetPath = folder + "/" + Path.GetFileName(file);
                if (!owned.Contains(assetPath)) orphans.Add(assetPath);
            }
        }

        private static bool Exists(string assetPath) =>
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null || File.Exists(assetPath);
    }
}
