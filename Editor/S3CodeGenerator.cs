using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 시트 스키마로부터 데이터 클래스({Table}Data)와 테이블 클래스({Table}DataTable), 그리고 이들을 모은
    /// 레지스트리를 만든다.
    /// 생성된 <c>ReadFrom</c>이 컬럼 이름을 직접 호출하므로 런타임에 리플렉션이 필요 없다.
    /// 내용이 이전과 같으면 파일을 건드리지 않아 불필요한 재컴파일이 일어나지 않는다.
    /// </summary>
    public static class S3CodeGenerator
    {
        /// <summary>생성되는 레지스트리 클래스 이름.</summary>
        public const string RegistryClassName = "S3TableRegistry";

        private const string FileHeader =
            "// ------------------------------------------------------------------\n" +
            "// 이 파일은 S3 Sheet Importer가 생성했습니다. 직접 수정하지 마세요.\n" +
            "// 시트를 고치고 Tools/KwonStudio/S3 임포터 에서 다시 임포트하면 갱신됩니다.\n" +
            "// ------------------------------------------------------------------\n";

        public static string DataClassName(string tableName) => tableName + "Data";

        public static string TableClassName(string tableName) => tableName + "DataTable";

        /// <summary>시트에서 나와야 할 스크립트 두 개의 경로와 내용.</summary>
        public readonly struct Sources
        {
            public readonly string DataPath;
            public readonly string DataText;
            public readonly string TablePath;
            public readonly string TableText;

            public Sources(string dataPath, string dataText, string tablePath, string tableText)
            {
                DataPath = dataPath;
                DataText = dataText;
                TablePath = tablePath;
                TableText = tableText;
            }
        }

        /// <summary>파일로 쓰지 않고 생성될 내용만 만든다. 동기화 확인이 이걸 쓴다.</summary>
        public static Sources Build(S3ImportSettings settings, S3Sheet sheet)
        {
            return new Sources(
                $"{settings.scriptOutputFolder}/{DataClassName(sheet.TableName)}.cs",
                BuildDataClass(settings, sheet),
                $"{settings.scriptOutputFolder}/{TableClassName(sheet.TableName)}.cs",
                BuildTableClass(settings, sheet));
        }

        /// <summary>스크립트를 쓴다. 실제로 내용이 바뀌었으면 true.</summary>
        public static bool Generate(S3ImportSettings settings, S3Sheet sheet)
        {
            S3Paths.EnsureDirectory(settings.scriptOutputFolder);

            var sources = Build(settings, sheet);
            var dataChanged = WriteIfChanged(sources.DataPath, sources.DataText);
            var tableChanged = WriteIfChanged(sources.TablePath, sources.TableText);

            return dataChanged || tableChanged;
        }

        /// <summary>디스크의 스크립트가 지금 시트와 일치하는지. 파일이 없으면 false.</summary>
        public static bool IsUpToDate(S3ImportSettings settings, S3Sheet sheet)
        {
            var sources = Build(settings, sheet);
            return Matches(sources.DataPath, sources.DataText) && Matches(sources.TablePath, sources.TableText);
        }

        private static bool Matches(string path, string expected)
        {
            if (!File.Exists(path)) return false;

            return File.ReadAllText(path).Replace("\r\n", "\n") == expected.Replace("\r\n", "\n");
        }

        private static string BuildDataClass(S3ImportSettings settings, S3Sheet sheet)
        {
            var key = sheet.KeyColumn;
            var sb = new StringBuilder();

            sb.Append(FileHeader);
            sb.Append("\nusing System;\nusing UnityEngine;\nusing KwonStudio.S3;\n\n");
            sb.Append($"namespace {settings.generatedNamespace}\n{{\n");
            sb.Append($"    /// <summary>'{sheet.TableName}' 시트의 한 행.</summary>\n");
            sb.Append("    [Serializable]\n");
            sb.Append($"    public class {DataClassName(sheet.TableName)} : IS3Row<{key.CSharpType}>\n    {{\n");

            foreach (var column in sheet.Columns)
            {
                sb.Append(BuildFieldDoc(column));
                sb.Append($"        public {column.CSharpType} {column.SourceName};\n\n");
            }

            sb.Append($"        /// <summary>테이블 조회용 키. 시트의 첫 컬럼 '{Escape(key.HeaderName)}' 이다.</summary>\n");
            sb.Append($"        public {key.CSharpType} Key => {key.SourceName};\n\n");

            sb.Append("        /// <summary>시트 한 행을 읽어 필드를 채운다. 컬럼은 이름으로 찾으므로 순서가 바뀌어도 된다.</summary>\n");
            sb.Append("        public void ReadFrom(S3RowReader reader)\n        {\n");
            foreach (var column in sheet.Columns)
            {
                sb.Append($"            {column.SourceName} = reader.{ReaderCall(column)};\n");
            }

            sb.Append("        }\n    }\n}\n");

            return sb.ToString();
        }

        private static string BuildTableClass(S3ImportSettings settings, S3Sheet sheet)
        {
            var key = sheet.KeyColumn;
            var tableClass = TableClassName(sheet.TableName);
            var sb = new StringBuilder();

            sb.Append(FileHeader);
            sb.Append("\nusing KwonStudio.S3;\n\n");
            sb.Append($"namespace {settings.generatedNamespace}\n{{\n");
            sb.Append($"    /// <summary>'{sheet.TableName}' 시트 전체를 담는 테이블.</summary>\n");
            sb.Append($"    public class {tableClass} : S3Table<{key.CSharpType}, {DataClassName(sheet.TableName)}>\n");
            sb.Append("    {\n    }\n}\n");

            return sb.ToString();
        }

        /// <summary>레지스트리 파일이 놓이는 경로.</summary>
        public static string RegistryPath(S3ImportSettings settings) =>
            $"{settings.scriptOutputFolder}/{RegistryClassName}.cs";

        /// <summary>레지스트리를 파일로 쓴다. 내용이 같으면 건드리지 않는다.</summary>
        /// <returns>파일이 바뀌었는지.</returns>
        public static bool GenerateRegistry(S3ImportSettings settings, IReadOnlyList<string> tableNames)
        {
            S3Paths.EnsureDirectory(settings.scriptOutputFolder);

            return WriteIfChanged(RegistryPath(settings), BuildRegistry(settings, tableNames));
        }

        /// <summary>
        /// 이름으로 테이블 인스턴스를 만드는 표를 생성한다.
        /// <para>
        /// <b>리플렉션을 쓰지 않는 이유</b> — 생성된 타입을 이름으로만 찾으면 IL2CPP 코드 스트리핑이
        /// 아무도 참조하지 않는 타입으로 보고 지워버린다. switch 로 <c>new</c> 를 직접 적으면 정적 참조가 남아 안전하다.
        /// </para>
        /// </summary>
        public static string BuildRegistry(S3ImportSettings settings, IReadOnlyList<string> tableNames)
        {
            var sb = new StringBuilder();

            sb.Append(FileHeader);
            sb.Append("\nusing System.Collections.Generic;\nusing KwonStudio.S3;\n\n");
            sb.Append($"namespace {settings.generatedNamespace}\n{{\n");
            sb.Append("    /// <summary>시트에서 만들어진 테이블 전부. 게임 시작 때 여기서 인스턴스를 만든다.</summary>\n");
            sb.Append($"    public static class {RegistryClassName}\n    {{\n");

            sb.Append("        /// <summary>등록된 테이블 이름. 배포 번들에서 찾을 키이기도 하다.</summary>\n");
            sb.Append("        public static readonly string[] TableNames =\n        {\n");
            foreach (var name in tableNames)
            {
                sb.Append($"            \"{TableClassName(name)}\",\n");
            }
            sb.Append("        };\n\n");

            sb.Append("        /// <summary>등록된 테이블을 전부 새로 만든다.</summary>\n");
            sb.Append("        public static List<S3TableBase> CreateAll()\n        {\n");
            sb.Append($"            return new List<S3TableBase>({tableNames.Count})\n            {{\n");
            foreach (var name in tableNames)
            {
                sb.Append($"                new {TableClassName(name)}(),\n");
            }
            sb.Append("            };\n        }\n    }\n}\n");

            return sb.ToString();
        }

        /// <summary>
        /// 필드에 붙일 XML 주석. 시트의 설명 행이 있으면 그 설명이 앞에 오고, 출처는 뒤에 남긴다.
        /// </summary>
        /// <remarks>
        /// 설명을 앞에 두는 이유는 IDE 툴팁이 첫 줄부터 보여주기 때문이다.
        /// 컬럼명·타입을 뒤에 남기는 건 이 필드가 시트의 어느 칸에서 왔는지 추적하기 위해서다.
        /// </remarks>
        private static string BuildFieldDoc(S3Column column)
        {
            var origin = $"시트 컬럼 '{Escape(column.HeaderName)}' ({Escape(column.RawType)})";

            if (string.IsNullOrEmpty(column.Description))
            {
                return $"        /// <summary>{origin}</summary>\n";
            }

            return "        /// <summary>\n" +
                   $"        /// {Escape(column.Description)}\n" +
                   $"        /// <para>{origin}</para>\n" +
                   "        /// </summary>\n";
        }

        /// <summary>컬럼 타입에 맞는 <see cref="S3RowReader"/> 호출 코드를 만든다.</summary>
        private static string ReaderCall(S3Column column)
        {
            var argument = $"\"{column.HeaderName.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

            if (column.IsCustomType)
            {
                // enum이나 직접 만든 타입은 제네릭 오버로드로 넘긴다.
                var method = column.IsArray ? "GetEnumArray" : "GetEnum";
                return $"{method}<{column.ElementType}>({argument})";
            }

            var suffix = column.IsArray ? "Array" : string.Empty;

            switch (column.ElementType)
            {
                case "int": return $"GetInt{suffix}({argument})";
                case "long": return $"GetLong{suffix}({argument})";
                case "float": return $"GetFloat{suffix}({argument})";
                case "double": return $"GetDouble{suffix}({argument})";
                case "bool": return $"GetBool{suffix}({argument})";
                case "string": return $"GetString{suffix}({argument})";
                case "Vector2": return $"GetVector2{suffix}({argument})";
                case "Vector3": return $"GetVector3{suffix}({argument})";
                case "Color": return $"GetColor{suffix}({argument})";
                default:
                    throw new S3Exception($"'{column.HeaderName}' 컬럼의 타입 {column.CSharpType} 은(는) 지원하지 않습니다.");
            }
        }

        /// <summary>줄바꿈만 다른 경우는 변경으로 보지 않는다.</summary>
        private static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Replace("\r\n", "\n");
                if (existing == content.Replace("\r\n", "\n")) return false;
            }

            File.WriteAllText(path, content);
            return true;
        }

        /// <summary>XML 주석 안에서 문서를 깨뜨릴 수 있는 문자를 치환한다.</summary>
        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
