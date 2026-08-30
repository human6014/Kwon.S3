using System.Collections.Generic;
using System.Text;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>시트 컬럼 하나에 대한 정보. 코드 생성에만 쓰인다.</summary>
    public sealed class S3Column
    {
        /// <summary>시트 1행에 적힌 원래 이름. 생성 코드가 이 이름으로 컬럼을 찾는다.</summary>
        public string HeaderName;

        /// <summary>리플렉션 없이 쓰는 실제 필드 이름.</summary>
        public string FieldName;

        /// <summary>생성 코드에 적히는 이름. 예약어면 앞에 @가 붙어 <see cref="FieldName"/>과 달라진다.</summary>
        public string SourceName;

        /// <summary>시트 2행에 적힌 원래 타입 표기.</summary>
        public string RawType;

        /// <summary>생성 코드에 그대로 박히는 C# 타입 이름. 배열이면 []까지 포함한다.</summary>
        public string CSharpType;

        /// <summary>배열 여부.</summary>
        public bool IsArray;

        /// <summary>배열이면 원소 타입, 아니면 <see cref="CSharpType"/>과 같다.</summary>
        public string ElementType;

        /// <summary>enum이나 직접 만든 타입처럼 내장 목록에 없는 타입인지.</summary>
        public bool IsCustomType;

        /// <summary>
        /// 설명 행에 적힌 사람이 읽을 설명. 생성 코드의 XML 주석이 된다. 없으면 빈 문자열.
        /// </summary>
        public string Description = string.Empty;
    }

    /// <summary>코드 생성을 위해 해석한 시트 한 장.</summary>
    public sealed class S3Sheet
    {
        public string TableName;
        public List<S3Column> Columns = new List<S3Column>();
        public int DataRowCount;

        /// <summary>첫 번째 유효 컬럼 = 키 컬럼.</summary>
        public S3Column KeyColumn => Columns[0];
    }

    /// <summary>
    /// CSV를 코드 생성용 스키마로 해석한다. 서식 규칙 자체는 런타임과 공유하려고 <see cref="S3Format"/>에 있다.
    /// 여기서 하는 일은 타입 표기를 C# 타입 이름으로 옮기고, 헤더를 쓸 수 있는 필드명으로 다듬는 것뿐이다.
    /// </summary>
    public static class S3Schema
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        // 생성되는 데이터 클래스가 이미 쓰고 있는 멤버 이름. 겹치면 뒤에 _를 붙여 피한다.
        private static readonly HashSet<string> ReservedMembers = new HashSet<string> { "Key", "ReadFrom" };

        /// <summary>내장 타입 표기 → C# 타입 이름.</summary>
        private static readonly Dictionary<string, string> BuiltInTypes = new Dictionary<string, string>
        {
            { "int", "int" }, { "int32", "int" },
            { "long", "long" }, { "int64", "long" },
            { "float", "float" }, { "single", "float" },
            { "double", "double" },
            { "bool", "bool" }, { "boolean", "bool" },
            { "string", "string" }, { "text", "string" },
            { "vector2", "Vector2" },
            { "vector3", "Vector3" },
            { "color", "Color" },
        };

        public static S3Sheet Parse(List<string[]> csv, string tableName, S3Layout layout)
        {
            if (csv.Count <= layout.TypeRowIndex)
            {
                throw new S3Exception(
                    $"[{tableName}] {layout} 부터 읽으려면 필드명 행({layout.HeaderRowNumber}행)과 타입 행({layout.HeaderRowNumber + 1}행)이 " +
                    $"필요한데 시트가 {csv.Count}줄뿐입니다. 시작 행/열 설정이나 gid를 확인하세요.");
            }

            var sheet = new S3Sheet { TableName = tableName };
            var headers = csv[layout.HeaderRowIndex];
            var types = csv[layout.TypeRowIndex];
            var descriptions = FindDescriptionRow(csv, layout);
            var usedFieldNames = new HashSet<string>();
            var usedHeaders = new HashSet<string>();
            var keyColumnIndex = -1;

            for (int i = layout.StartColumnIndex; i < headers.Length; i++)
            {
                var header = headers[i].Trim();
                var rawType = i < types.Length ? types[i].Trim() : string.Empty;

                if (S3Format.IsIgnoredHeader(header) || S3Format.IsIgnoredType(rawType)) continue;

                if (!usedHeaders.Add(S3Format.NormalizeHeader(header)))
                {
                    throw new S3Exception($"[{tableName}] 컬럼 이름이 중복됩니다: '{header}'");
                }

                var fieldName = ToFieldName(header);
                if (!usedFieldNames.Add(fieldName))
                {
                    throw new S3Exception(
                        $"[{tableName}] '{header}' 이(가) 다른 컬럼과 같은 필드명({fieldName})으로 바뀝니다. 컬럼 이름을 구분되게 바꿔주세요.");
                }

                var column = BuildColumn(header, fieldName, rawType, tableName);

                // 키 컬럼 칸은 설명 행임을 알리려고 비우거나 #을 붙인 자리다.
                // #을 붙였다면 그 뒤가 키 컬럼의 설명이다.
                bool isKeyColumn = keyColumnIndex < 0;
                column.Description = descriptions != null && i < descriptions.Length
                    ? Flatten(descriptions[i], isKeyColumn)
                    : string.Empty;

                sheet.Columns.Add(column);
                if (isKeyColumn) keyColumnIndex = i;
            }

            if (sheet.Columns.Count == 0)
            {
                throw new S3Exception(
                    $"[{tableName}] {layout} 부터 읽었지만 쓸 수 있는 컬럼이 없습니다. " +
                    $"{layout.HeaderRowNumber}행에 필드명, {layout.HeaderRowNumber + 1}행에 타입(int, float, string ...)을 적어주세요.");
            }

            // 행 수 세는 규칙은 런타임 파서와 같아야 한다. 키 칸이 비었거나 #이면 데이터가 아니다.
            for (int r = layout.DataStartRowIndex; r < csv.Count; r++)
            {
                var row = csv[r];
                var keyCell = keyColumnIndex < row.Length ? row[keyColumnIndex] : string.Empty;
                if (S3Format.IsSkippedDataRow(keyCell)) continue;

                sheet.DataRowCount++;
            }

            return sheet;
        }

        /// <summary>
        /// 타입 행 바로 다음 줄이 데이터가 아니면 그 줄을 <b>설명 행</b>으로 본다.
        /// </summary>
        /// <remarks>
        /// 기존 시트를 건드리지 않으려고 새 규칙을 만들지 않고 <b>메모 줄 규칙에 얹었다.</b>
        /// 키 칸이 비었거나 <c>#</c>으로 시작하는 줄은 이미 런타임·에디터 양쪽이 데이터가 아니라고 보므로,
        /// 설명 행을 넣어도 데이터 파싱은 한 글자도 바뀌지 않는다.
        /// 설명 행이 없는 시트는 그냥 설명이 비는 것뿐이다.
        /// </remarks>
        /// <returns>설명 행. 없으면 null.</returns>
        private static string[] FindDescriptionRow(List<string[]> csv, S3Layout layout)
        {
            int index = layout.DataStartRowIndex;
            if (index >= csv.Count) return null;

            var row = csv[index];

            // 키 컬럼이 어디인지는 아직 모르므로, 시작 열부터 첫 유효 칸을 키로 본다.
            // 설명 행은 어차피 키 칸을 비우거나 #으로 시작해야 하므로 이 판정으로 충분하다.
            var keyCell = layout.StartColumnIndex < row.Length ? row[layout.StartColumnIndex] : string.Empty;

            return S3Format.IsSkippedDataRow(keyCell) ? row : null;
        }

        /// <summary>설명을 XML 주석 한 줄에 넣을 수 있게 다듬는다.</summary>
        /// <param name="stripCommentPrefix">
        /// 키 컬럼 칸이면 true. 그 칸은 설명 행임을 알리는 <c>#</c>이 앞에 붙으므로 떼어낸다.
        /// </param>
        private static string Flatten(string text, bool stripCommentPrefix)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var flat = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

            if (stripCommentPrefix && flat.StartsWith(S3Format.CommentPrefix))
            {
                flat = flat.Substring(S3Format.CommentPrefix.Length).Trim();
            }

            return flat;
        }

        private static S3Column BuildColumn(string header, string fieldName, string rawType, string tableName)
        {
            var isArray = rawType.EndsWith("[]");
            var core = (isArray ? rawType.Substring(0, rawType.Length - 2) : rawType).Trim();

            if (core.Length == 0)
            {
                throw new S3Exception($"[{tableName}] '{header}' 컬럼의 타입 표기가 비어 있습니다.");
            }

            // 내장 목록에 없으면 enum이나 직접 만든 타입으로 보고 적힌 이름을 그대로 쓴다.
            var isCustom = !BuiltInTypes.TryGetValue(core.ToLowerInvariant(), out var element);
            if (isCustom) element = core;

            return new S3Column
            {
                HeaderName = header,
                FieldName = fieldName,
                SourceName = Keywords.Contains(fieldName) ? "@" + fieldName : fieldName,
                RawType = rawType,
                CSharpType = isArray ? element + "[]" : element,
                IsArray = isArray,
                ElementType = element,
                IsCustomType = isCustom,
            };
        }

        /// <summary>'몬스터 이름'이나 'atk speed' 같은 헤더를 유효한 C# 식별자로 만든다.</summary>
        public static string ToFieldName(string header)
        {
            var sb = new StringBuilder(header.Length);
            var upperNext = false;

            foreach (var c in header)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    // 공백이나 기호는 버리고 다음 글자를 대문자로 이어 붙인다. ("atk speed" → "atkSpeed")
                    upperNext = sb.Length > 0;
                }
            }

            if (sb.Length == 0) sb.Append("column");
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');

            var name = sb.ToString();
            return ReservedMembers.Contains(name) ? name + "_" : name;
        }
    }
}
