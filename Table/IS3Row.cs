namespace KwonStudio.S3
{
    /// <summary>
    /// 시트 한 행에 대응하는 데이터 클래스가 구현하는 인터페이스.
    /// 임포터가 코드를 생성할 때 자동으로 붙여주므로 직접 구현할 일은 거의 없다.
    /// </summary>
    /// <typeparam name="TKey">키 컬럼의 타입. 보통 int 또는 string.</typeparam>
    public interface IS3Row<out TKey>
    {
        /// <summary>테이블 조회에 쓰이는 키. 시트의 첫 유효 컬럼 값이다.</summary>
        TKey Key { get; }

        /// <summary>
        /// 시트의 한 행을 자기 필드에 채운다.
        /// 생성 코드가 컬럼 이름을 문자열로 직접 호출하므로 리플렉션이 필요 없고,
        /// 에디터 굽기와 런타임 다운로드가 완전히 같은 경로를 탄다.
        /// </summary>
        void ReadFrom(S3RowReader reader);
    }
}
