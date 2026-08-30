using System.Text;

namespace KwonStudio.S3
{
    /// <summary>
    /// 테이블 묶음을 한 번 갱신한 결과.
    /// 폴백이 없으므로 하나라도 실패하면 게임을 진행시키지 않는다. 그 판단과 화면에 띄울 문구가 여기서 나온다.
    /// </summary>
    public readonly struct S3LoadReport
    {
        public S3LoadReport(int total, int failed, string firstError, string failedTableNames)
        {
            Total = total;
            Failed = failed;
            FirstError = firstError;
            FailedTableNames = failedTableNames;
        }

        /// <summary>갱신을 시도한 테이블 수.</summary>
        public int Total { get; }

        public int Failed { get; }

        public int Succeeded => Total - Failed;

        /// <summary>처음 만난 실패 사유. 보통 나머지도 같은 원인이다.</summary>
        public string FirstError { get; }

        /// <summary>실패한 테이블 이름을 쉼표로 이은 것. 너무 길면 앞쪽만.</summary>
        public string FailedTableNames { get; }

        /// <summary>전부 받아왔는지. 이게 false 면 게임을 시작하지 않는다.</summary>
        public bool IsSuccess => Total > 0 && Failed == 0;

        /// <summary>테이블이 하나도 등록돼 있지 않은 상태. 설정 사고라 실패와 구분해서 보여준다.</summary>
        public bool IsEmpty => Total == 0;

        /// <summary>에러 화면에 그대로 띄울 수 있는 설명.</summary>
        public string BuildMessage()
        {
            if (IsEmpty) return "갱신할 테이블이 등록돼 있지 않습니다.\nS3Database 에셋의 목록을 확인해주세요.";
            if (IsSuccess) return $"테이블 {Total}개를 모두 받았습니다.";

            var builder = new StringBuilder();
            builder.Append("데이터를 받지 못했습니다. (")
                   .Append(Failed).Append('/').Append(Total).Append(")\n\n");

            if (!string.IsNullOrEmpty(FailedTableNames))
            {
                builder.Append(FailedTableNames).Append('\n');
            }

            if (!string.IsNullOrEmpty(FirstError))
            {
                builder.Append('\n').Append(FirstError);
            }

            return builder.ToString();
        }
    }
}
