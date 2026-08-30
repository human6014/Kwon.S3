namespace KwonStudio.S3
{
    /// <summary>
    /// 시트 서식 규칙을 한곳에 모아둔다.
    /// 에디터의 코드 생성기와 런타임 파서가 같은 규칙을 봐야 하므로 두 쪽 모두 여기를 참조한다.
    /// 읽기 시작 위치는 시트마다 다를 수 있어 <see cref="S3Layout"/>이 따로 들고 있다.
    /// </summary>
    public static class S3Format
    {
        /// <summary>필드명 행에서 타입 행까지의 거리.</summary>
        public const int HeaderToTypeOffset = 1;

        /// <summary>필드명 행에서 첫 데이터 행까지의 거리.</summary>
        public const int HeaderToDataOffset = 2;

        /// <summary>배열 값을 감싸는 여는 괄호. 예: <c>[0.1, 0.2]</c></summary>
        public const char ArrayOpen = '[';

        /// <summary>배열 값을 닫는 괄호.</summary>
        public const char ArrayClose = ']';

        /// <summary>배열 원소 구분자.</summary>
        public const char ArraySeparator = ',';

        /// <summary>예전에 쓰던 구분자. 지금은 지원하지 않고, 발견하면 새 형식을 안내한다.</summary>
        public const char LegacyArraySeparator = '|';

        /// <summary>이 문자로 시작하는 헤더/행은 무시된다.</summary>
        public const string CommentPrefix = "#";

        /// <summary>타입 칸에 이 값을 적으면 컬럼을 통째로 무시한다.</summary>
        public const string IgnoreType = "-";

        /// <summary>사람이 적은 헤더를 비교용으로 정규화한다. 대소문자와 앞뒤 공백을 무시한다.</summary>
        public static string NormalizeHeader(string header) =>
            header == null ? string.Empty : header.Trim().ToLowerInvariant();

        /// <summary>필드명 칸이 비었거나 #으로 시작하면 그 컬럼은 쓰지 않는다.</summary>
        public static bool IsIgnoredHeader(string header) =>
            string.IsNullOrWhiteSpace(header) || header.TrimStart().StartsWith(CommentPrefix);

        /// <summary>타입 칸이 비었거나 -면 그 컬럼은 쓰지 않는다.</summary>
        public static bool IsIgnoredType(string type) =>
            string.IsNullOrWhiteSpace(type) || type.Trim() == IgnoreType;

        /// <summary>
        /// 데이터 행인지 아닌지를 키 컬럼 칸 하나로 판단한다.
        /// 키가 비어 있으면 데이터가 아니라고 보므로, 데이터 블록 아래에 적어둔 메모 줄은 저절로 걸러진다.
        /// 키 칸에 글자를 적어야 하는 메모라면 앞에 #을 붙이면 된다.
        /// </summary>
        public static bool IsSkippedDataRow(string keyCell) =>
            string.IsNullOrWhiteSpace(keyCell) || keyCell.TrimStart().StartsWith(CommentPrefix);
    }
}
