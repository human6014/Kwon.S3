using System.IO;
using UnityEditor;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>폴더 생성과 캐시 파일 경로를 다루는 잡일 모음.</summary>
    public static class S3Paths
    {
        /// <summary>다운로드한 CSV 원본을 두는 곳. Library 아래라 git에 올라가지 않는다.</summary>
        public const string CacheFolder = "Library/S3Cache";

        /// <summary>
        /// "Assets/A/B/C" 같은 경로의 폴더를 AssetDatabase에 등록된 상태로 만든다.
        /// <see cref="AssetDatabase.CreateAsset"/>는 부모 폴더가 등록되어 있지 않으면 실패한다.
        /// 테이블은 에셋이 아니지만 임포터 설정(<see cref="S3ImportSettings"/>)은 여전히 에셋이라 이게 필요하다.
        /// </summary>
        public static void EnsureAssetFolder(string folder)
        {
            folder = Normalize(folder);
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            var current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>스크립트 출력 폴더처럼 AssetDatabase 등록이 급하지 않은 경우에 쓴다.</summary>
        public static void EnsureDirectory(string folder)
        {
            folder = Normalize(folder);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }

        public static string CacheFilePath(string tableName) => $"{CacheFolder}/{tableName}.csv";

        /// <summary>다운로드한 CSV를 캐시에 남긴다. 컴파일 뒤 이어서 에셋을 만들 때 재사용한다.</summary>
        public static void WriteCache(string tableName, string csv)
        {
            EnsureDirectory(CacheFolder);
            File.WriteAllText(CacheFilePath(tableName), csv);
        }

        /// <summary>캐시된 CSV를 읽는다. 없으면 null.</summary>
        public static string ReadCache(string tableName)
        {
            var path = CacheFilePath(tableName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static string Normalize(string path) => path?.Replace('\\', '/').TrimEnd('/');
    }
}
