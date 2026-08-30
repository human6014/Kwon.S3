using System;

namespace KwonStudio.S3
{
    /// <summary>
    /// 시트 데이터가 규칙에 맞지 않을 때 던진다.
    /// 사람이 시트를 고쳐서 해결할 수 있는 문제만 이 예외를 쓰고,
    /// 메시지에는 항상 테이블 이름과 시트 행 번호를 넣어 어디를 고쳐야 할지 바로 알 수 있게 한다.
    /// </summary>
    public class S3Exception : Exception
    {
        public S3Exception(string message) : base(message) { }
    }
}
