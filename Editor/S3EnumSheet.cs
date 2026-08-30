namespace KwonStudio.S3.EditorTools
{
    /// <summary>열거형 값 하나.</summary>
    public sealed class S3EnumMember
    {
        public string Name;
        public long Value;
        public string Comment;
    }

    /// <summary>시트 탭 하나에서 읽어낸 열거형.</summary>
    public sealed class S3EnumDefinition
    {
        public string Name;
        public string Gid;
        public bool IsFlags;
        public readonly System.Collections.Generic.List<S3EnumMember> Members =
            new System.Collections.Generic.List<S3EnumMember>();
    }

    /// <summary>
    /// 열거형 문서의 탭 하나를 읽는다. <b>탭 하나가 enum 하나</b>이고, 탭 이름이 곧 enum 이름이다.
    /// 서식은 데이터 시트와 똑같아서 <see cref="S3TextTable"/>·<see cref="S3RowReader"/>를 그대로 쓰고,
    /// 시작 행/열과 메모 무시 규칙도 동일하게 적용된다.
    ///
    /// <code>
    ///   MonsterGrade 탭
    ///     name     value   flags   comment
    ///     string   int     bool    string
    ///     Normal                   일반
    ///     Elite                    정예
    ///     Boss     10              보스
    /// </code>
    /// </summary>
    public static class S3EnumSheet
    {
        public const string NameColumn = "name";
        public const string ValueColumn = "value";
        public const string FlagsColumn = "flags";
        public const string CommentColumn = "comment";

        /// <summary>탭 CSV 하나를 열거형 정의로 바꾼다.</summary>
        /// <param name="csv">탭의 CSV 본문.</param>
        /// <param name="tabName">탭 이름. 그대로 enum 이름이 된다.</param>
        /// <param name="gid">탭 gid. 오류 메시지와 로그에만 쓴다.</param>
        /// <param name="layout">읽기 시작 위치.</param>
        public static S3EnumDefinition Parse(string csv, string tabName, string gid, S3Layout layout)
        {
            var enumName = S3TabParser.ToTableName(tabName);
            var table = S3TextTable.Parse(csv, enumName, layout);

            if (!table.TryGetColumnIndex(NameColumn, out _))
            {
                throw new S3Exception(
                    $"[{enumName}] '{NameColumn}' 컬럼이 필요합니다. {layout.HeaderRowNumber}행에 name 을 적고 그 아래 줄에 타입(string)을 적어주세요.");
            }

            var hasValue = table.TryGetColumnIndex(ValueColumn, out _);
            var hasFlags = table.TryGetColumnIndex(FlagsColumn, out _);
            var hasComment = table.TryGetColumnIndex(CommentColumn, out _);

            var definition = new S3EnumDefinition { Name = enumName, Gid = gid };
            var reader = new S3RowReader(table);

            for (int i = 0; i < table.RowCount; i++)
            {
                reader.MoveTo(i);
                var sheetRow = table.ToSheetRowNumber(i);

                // 키 컬럼(= name)이 비면 애초에 데이터 행으로 잡히지 않으므로 여기 온 값은 항상 비어 있지 않다.
                var memberRaw = reader.GetString(NameColumn);

                if (hasFlags)
                {
                    var flagsRaw = reader.GetString(FlagsColumn);
                    if (!string.IsNullOrWhiteSpace(flagsRaw) && S3Parse.Bool(flagsRaw)) definition.IsFlags = true;
                }

                // PascalCase로 다듬으므로 첫 글자가 항상 대문자다. C# 예약어는 전부 소문자라 충돌하지 않는다.
                var memberName = S3TabParser.ToTableName(memberRaw);

                foreach (var existing in definition.Members)
                {
                    if (existing.Name != memberName) continue;

                    throw new S3Exception($"[{enumName}] {sheetRow}행: '{memberName}' 이(가) 이미 있습니다.");
                }

                definition.Members.Add(new S3EnumMember
                {
                    Name = memberName,
                    Value = ResolveValue(hasValue ? reader.GetString(ValueColumn) : string.Empty, definition, sheetRow),
                    Comment = hasComment ? reader.GetString(CommentColumn) : string.Empty,
                });
            }

            if (definition.Members.Count == 0)
            {
                throw new S3Exception($"[{enumName}] 값이 하나도 없습니다. 시작 행/열 설정을 확인하세요.");
            }

            return definition;
        }

        /// <summary>
        /// value 칸이 비어 있으면 자동으로 매긴다.
        /// 보통은 직전 값 + 1이고, flags면 아직 안 쓴 2의 거듭제곱을 준다.
        /// </summary>
        private static long ResolveValue(string raw, S3EnumDefinition definition, int sheetRow)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    return S3Parse.Long(raw);
                }
                catch (S3Exception e)
                {
                    throw new S3Exception($"[{definition.Name}] {sheetRow}행 '{ValueColumn}' 칸: {e.Message}");
                }
            }

            if (definition.IsFlags)
            {
                long candidate = 1;
                while (UsesValue(definition, candidate))
                {
                    candidate <<= 1;
                    if (candidate > 0) continue;

                    throw new S3Exception($"[{definition.Name}] {sheetRow}행: flags 값이 64개를 넘었습니다.");
                }

                return candidate;
            }

            var count = definition.Members.Count;
            return count == 0 ? 0 : definition.Members[count - 1].Value + 1;
        }

        private static bool UsesValue(S3EnumDefinition definition, long value)
        {
            foreach (var member in definition.Members)
            {
                if (member.Value == value) return true;
            }

            return false;
        }
    }
}
