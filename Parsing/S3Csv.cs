using System.Collections.Generic;
using System.Text;

namespace KwonStudio.S3
{
    /// <summary>
    /// RFC 4180 규칙의 CSV 파서. 큰따옴표로 감싼 셀, 셀 안의 쉼표/줄바꿈, 이스케이프된 따옴표("")를 처리한다.
    /// 시트에 줄바꿈이 들어간 설명 컬럼이 있어도 행이 깨지지 않는다.
    /// </summary>
    public static class S3Csv
    {
        private const char Bom = (char)0xFEFF;

        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;

            // 구글이 붙여 보내는 BOM 제거.
            if (text[0] == Bom) text = text.Substring(1);
            if (text.Length == 0) return rows;

            var cells = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (inQuotes)
                {
                    if (c != '"')
                    {
                        cell.Append(c);
                        continue;
                    }

                    // "" 는 따옴표 한 글자, 그 외엔 인용 구간의 끝.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;

                    case ',':
                        cells.Add(cell.ToString());
                        cell.Clear();
                        break;

                    case '\r':
                        // \r\n 도 \r 단독도 한 번의 줄바꿈으로 본다.
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        EndRow(rows, cells, cell);
                        break;

                    case '\n':
                        EndRow(rows, cells, cell);
                        break;

                    default:
                        cell.Append(c);
                        break;
                }
            }

            // 마지막 줄에 개행이 없을 수도 있다.
            if (cell.Length > 0 || cells.Count > 0) EndRow(rows, cells, cell);

            return rows;
        }

        private static void EndRow(List<string[]> rows, List<string> cells, StringBuilder cell)
        {
            cells.Add(cell.ToString());
            cell.Clear();
            rows.Add(cells.ToArray());
            cells.Clear();
        }
    }
}
