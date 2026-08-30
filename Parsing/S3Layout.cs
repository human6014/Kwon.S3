using System;
using System.Text;
using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>
    /// 시트의 어디서부터 읽을지 정한다.
    /// 시트에는 데이터 말고도 기획 메모나 설명이 함께 들어가므로,
    /// 데이터 블록의 왼쪽 위 모서리(= 필드명 행과 시작 열)를 지정할 수 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 시작 위치를 A1이 아니라 C5로 잡으면 이렇게 읽는다.
    /// <code>
    ///   5행: C열부터 오른쪽으로 필드명
    ///   6행: C열부터 오른쪽으로 타입
    ///   7행~: 데이터
    ///   A, B열과 1~4행은 아예 보지 않는다.
    /// </code>
    /// </remarks>
    [Serializable]
    public struct S3Layout
    {
        [SerializeField, Tooltip("필드명이 적힌 행 번호. 시트에 보이는 그대로 1부터 센다.")]
        private int _headerRow;

        [SerializeField, Tooltip("데이터가 시작되는 열. A, B, AA 처럼 적거나 1, 2 처럼 숫자로 적어도 된다.")]
        private string _startColumn;

        public S3Layout(int headerRow, string startColumn)
        {
            _headerRow = headerRow;
            _startColumn = startColumn;
        }

        /// <summary>A1에서 시작하는 기본 배치.</summary>
        public static S3Layout Default => new S3Layout(1, "A");

        /// <summary>필드명 행 번호(1부터). 설정이 비어 있으면 1행으로 본다.</summary>
        public int HeaderRowNumber => _headerRow > 0 ? _headerRow : 1;

        /// <summary>필드명 행의 배열 인덱스(0부터).</summary>
        public int HeaderRowIndex => HeaderRowNumber - 1;

        /// <summary>타입 행의 배열 인덱스.</summary>
        public int TypeRowIndex => HeaderRowIndex + S3Format.HeaderToTypeOffset;

        /// <summary>첫 데이터 행의 배열 인덱스.</summary>
        public int DataStartRowIndex => HeaderRowIndex + S3Format.HeaderToDataOffset;

        /// <summary>시작 열의 배열 인덱스(0부터). 설정이 비었거나 잘못됐으면 A열로 본다.</summary>
        public int StartColumnIndex => TryParseColumn(_startColumn, out var index) ? index : 0;

        /// <summary>시작 열을 A1 표기로 정규화한 것. 숫자로 적었어도 문자로 돌려준다.</summary>
        public string StartColumnName => IndexToColumn(StartColumnIndex);

        /// <summary>설정에 적힌 원래 문자열. 에디터 입력창이 타이핑 중인 값을 그대로 보여줘야 해서 필요하다.</summary>
        public string StartColumnText => _startColumn ?? string.Empty;

        /// <summary>설정 문자열이 열 표기로 읽히는지. 창에서 잘못 입력한 경우를 알려주는 데 쓴다.</summary>
        public bool IsStartColumnValid =>
            string.IsNullOrWhiteSpace(_startColumn) || TryParseColumn(_startColumn, out _);

        /// <summary>"C5" 처럼 사람이 읽는 좌표.</summary>
        public override string ToString() => $"{StartColumnName}{HeaderRowNumber}";

        /// <summary>"A", "c", "AB" 또는 "1", "3" 을 0부터 세는 열 인덱스로 바꾼다.</summary>
        public static bool TryParseColumn(string column, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(column)) return true; // 비우면 A열

            column = column.Trim();

            // 숫자로 적었다면 1부터 세는 열 번호로 본다.
            if (int.TryParse(column, out var number))
            {
                if (number < 1) return false;

                index = number - 1;
                return true;
            }

            var value = 0;
            foreach (var c in column)
            {
                var upper = char.ToUpperInvariant(c);
                if (upper < 'A' || upper > 'Z') return false;

                value = value * 26 + (upper - 'A' + 1);
            }

            index = value - 1;
            return true;
        }

        /// <summary>0부터 세는 열 인덱스를 A1 표기로 바꾼다. (0 → A, 26 → AA)</summary>
        public static string IndexToColumn(int index)
        {
            if (index < 0) index = 0;

            var sb = new StringBuilder(3);
            var value = index + 1;

            while (value > 0)
            {
                var remainder = (value - 1) % 26;
                sb.Insert(0, (char)('A' + remainder));
                value = (value - 1) / 26;
            }

            return sb.ToString();
        }
    }
}
