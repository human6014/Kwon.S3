using System.Collections.Generic;

namespace KwonStudio.S3
{
    /// <summary>
    /// CSV를 "헤더 → 컬럼 위치" 사전과 데이터 행 목록으로 정리한 것.
    /// 컬럼을 이름으로 찾으므로 시트에서 컬럼 순서를 바꾸거나 중간에 새 컬럼을 끼워 넣어도
    /// 이미 배포된 빌드가 데이터를 계속 읽을 수 있다.
    /// </summary>
    public sealed class S3TextTable
    {
        private readonly Dictionary<string, int> _columns;
        private readonly List<string[]> _rows;

        // 메모/주석 행을 건너뛰어도 오류 메시지에 시트의 실제 행 번호를 보여주기 위한 대응표.
        private readonly List<int> _sourceRowNumbers;

        /// <summary>오류 메시지에 쓰는 테이블 이름.</summary>
        public string Name { get; }

        public IReadOnlyList<string[]> Rows => _rows;

        public int RowCount => _rows.Count;

        private S3TextTable(string name, Dictionary<string, int> columns, List<string[]> rows, List<int> sourceRowNumbers)
        {
            Name = name;
            _columns = columns;
            _rows = rows;
            _sourceRowNumbers = sourceRowNumbers;
        }

        /// <summary>
        /// CSV 본문을 <paramref name="layout"/>이 가리키는 위치부터 해석한다.
        /// 시작 좌표를 기준으로 첫 줄이 필드명, 그 다음 줄이 타입, 이후가 데이터다.
        /// </summary>
        public static S3TextTable Parse(string csv, string name, S3Layout layout)
        {
            var cells = S3Csv.Parse(csv);

            if (cells.Count <= layout.TypeRowIndex)
            {
                throw new S3Exception(
                    $"[{name}] {layout} 부터 읽으려면 필드명 행({layout.HeaderRowNumber}행)과 타입 행({layout.HeaderRowNumber + 1}행)이 필요한데 " +
                    $"시트가 {cells.Count}줄뿐입니다. 시작 행/열 설정이나 gid를 확인하세요.");
            }

            var headers = cells[layout.HeaderRowIndex];
            var types = cells[layout.TypeRowIndex];
            var columns = new Dictionary<string, int>(headers.Length);
            var keyColumnIndex = -1;

            for (int i = layout.StartColumnIndex; i < headers.Length; i++)
            {
                var header = headers[i];
                var type = i < types.Length ? types[i] : string.Empty;

                if (S3Format.IsIgnoredHeader(header) || S3Format.IsIgnoredType(type)) continue;

                var key = S3Format.NormalizeHeader(header);
                if (!columns.ContainsKey(key)) columns.Add(key, i);

                if (keyColumnIndex < 0) keyColumnIndex = i;
            }

            if (keyColumnIndex < 0)
            {
                throw new S3Exception(
                    $"[{name}] {layout} 부터 읽었지만 쓸 수 있는 컬럼이 없습니다. " +
                    $"{layout.HeaderRowNumber}행에 필드명, {layout.HeaderRowNumber + 1}행에 타입을 적었는지 확인하세요.");
            }

            var rows = new List<string[]>();
            var sourceRowNumbers = new List<int>();

            for (int r = layout.DataStartRowIndex; r < cells.Count; r++)
            {
                var row = cells[r];
                var keyCell = keyColumnIndex < row.Length ? row[keyColumnIndex] : string.Empty;

                // 키 칸이 비었거나 #으로 시작하면 데이터가 아니다. 시트에 섞인 메모 줄이 여기서 걸러진다.
                if (S3Format.IsSkippedDataRow(keyCell)) continue;

                rows.Add(row);
                sourceRowNumbers.Add(r + 1); // 시트는 1행부터 센다.
            }

            return new S3TextTable(name, columns, rows, sourceRowNumbers);
        }

        /// <summary>헤더 이름으로 컬럼 위치를 찾는다. 대소문자와 앞뒤 공백은 무시한다.</summary>
        public bool TryGetColumnIndex(string header, out int index) =>
            _columns.TryGetValue(S3Format.NormalizeHeader(header), out index);

        /// <summary>셀 값. 행이 짧아 칸이 없으면 빈 문자열.</summary>
        public string GetCell(int rowIndex, int columnIndex)
        {
            var row = _rows[rowIndex];
            return columnIndex < row.Length ? row[columnIndex] : string.Empty;
        }

        /// <summary>데이터 행 번호를 사람이 시트에서 보는 행 번호(1-based)로 바꾼다.</summary>
        public int ToSheetRowNumber(int rowIndex) => _sourceRowNumbers[rowIndex];
    }
}
