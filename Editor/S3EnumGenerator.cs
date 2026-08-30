using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 시트에서 읽은 열거형을 C# 파일로 만든다.
    /// 데이터 클래스와 같은 네임스페이스·어셈블리에 넣으므로, 시트 타입 칸에 이름만 적으면 바로 쓰인다.
    /// </summary>
    public static class S3EnumGenerator
    {
        /// <summary>생성 스크립트 폴더 아래 열거형만 모아두는 하위 폴더.</summary>
        public const string SubFolder = "Enums";

        private const string FileHeader =
            "// ------------------------------------------------------------------\n" +
            "// 이 파일은 S3 Sheet Importer가 생성했습니다. 직접 수정하지 마세요.\n" +
            "// 값은 Enums 시트에서 관리합니다.\n" +
            "// ------------------------------------------------------------------\n";

        public static string FolderPath(S3ImportSettings settings) => $"{settings.scriptOutputFolder}/{SubFolder}";

        public static string FilePath(S3ImportSettings settings, string enumName) =>
            $"{FolderPath(settings)}/{enumName}.cs";

        /// <summary>열거형 파일을 쓰고 필요 없어진 파일을 지운다. 실제로 바뀐 게 있으면 true.</summary>
        public static bool Generate(S3ImportSettings settings, List<S3EnumDefinition> definitions, List<string> deletedPaths)
        {
            S3Paths.EnsureDirectory(FolderPath(settings));

            var changed = false;

            foreach (var definition in definitions)
            {
                if (WriteIfChanged(FilePath(settings, definition.Name), Build(settings, definition))) changed = true;
            }

            foreach (var path in FindOrphans(settings))
            {
                if (!AssetDatabase.DeleteAsset(path)) continue;

                deletedPaths?.Add(path);
                changed = true;
            }

            return changed;
        }

        /// <summary>해당 enum 파일을 지운다. 이름이 바뀌었을 때 예전 파일을 치우는 데 쓴다.</summary>
        public static bool DeleteFile(S3ImportSettings settings, string enumName)
        {
            if (string.IsNullOrWhiteSpace(enumName)) return false;

            var path = FilePath(settings, enumName);
            return File.Exists(path) && AssetDatabase.DeleteAsset(path);
        }

        /// <summary>디스크의 열거형 파일이 지금 시트와 일치하는지.</summary>
        public static bool IsUpToDate(S3ImportSettings settings, List<S3EnumDefinition> definitions)
        {
            if (FindOrphans(settings).Count > 0) return false;

            foreach (var definition in definitions)
            {
                var path = FilePath(settings, definition.Name);
                if (!File.Exists(path)) return false;

                if (Normalize(File.ReadAllText(path)) != Normalize(Build(settings, definition))) return false;
            }

            return true;
        }

        /// <summary>열거형 폴더에 있지만 설정에 등록된 enum 이 아닌 파일. 다운로드 없이 판단할 수 있다.</summary>
        public static List<string> FindOrphans(S3ImportSettings settings)
        {
            var alive = new HashSet<string>();
            foreach (var entry in settings.enums)
            {
                if (!string.IsNullOrWhiteSpace(entry.enumName)) alive.Add(entry.enumName);
            }

            return FindOrphans(settings, alive);
        }

        private static List<string> FindOrphans(S3ImportSettings settings, HashSet<string> alive)
        {
            var orphans = new List<string>();
            var folder = FolderPath(settings);
            if (!Directory.Exists(folder)) return orphans;

            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (alive.Contains(name)) continue;

                orphans.Add(folder + "/" + Path.GetFileName(file));
            }

            return orphans;
        }

        private static string Build(S3ImportSettings settings, S3EnumDefinition definition)
        {
            var sb = new StringBuilder();

            sb.Append(FileHeader);
            sb.Append($"\nnamespace {settings.generatedNamespace}\n{{\n");
            sb.Append($"    /// <summary>enum 문서의 '{definition.Name}' 탭(gid {definition.Gid})에서 생성된 열거형.</summary>\n");

            if (definition.IsFlags) sb.Append("    [System.Flags]\n");

            sb.Append($"    public enum {definition.Name}\n    {{\n");

            foreach (var member in definition.Members)
            {
                if (!string.IsNullOrWhiteSpace(member.Comment))
                {
                    sb.Append($"        /// <summary>{Escape(member.Comment)}</summary>\n");
                }

                sb.Append($"        {member.Name} = {member.Value},\n");
            }

            sb.Append("    }\n}\n");

            return sb.ToString();
        }

        private static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && Normalize(File.ReadAllText(path)) == Normalize(content)) return false;

            File.WriteAllText(path, content);
            return true;
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
