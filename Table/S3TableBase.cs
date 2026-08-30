using System;

namespace KwonStudio.S3
{
    /// <summary>
    /// 시트에서 만들어진 모든 테이블의 공통 베이스.
    /// 제네릭이 아니므로 로더가 타입을 몰라도 이걸로 다룰 수 있다.
    /// 실제 행 데이터는 <see cref="S3Table{TKey,TRow}"/>가 들고 있다.
    /// </summary>
    /// <remarks>
    /// <b>ScriptableObject 가 아니다.</b> 데이터는 전부 배포본에서 받아 채우므로 에셋으로 구울 것이 없고,
    /// 굽지 않으니 빌드에서 비울 일도, 플레이 모드에서 에셋이 더러워질 일도 없다.
    /// 인스턴스는 <c>S3TableRegistry</c>(생성물)가 만들고 <c>S3Loader</c>가 들고 있는다.
    /// </remarks>
    public abstract class S3TableBase
    {
        private string _tableName;

        /// <summary>
        /// 이 테이블의 이름. 클래스 이름을 그대로 쓴다(예: MonsterDataTable).
        /// <b>배포 번들에서 자기 몫을 찾는 키</b>이기도 하므로, 클래스 이름을 바꾸면 번들 쪽도 함께 바뀌어야 한다.
        /// </summary>
        public string TableName => _tableName ??= GetType().Name;

        /// <summary>이 테이블이 담는 행 데이터의 타입.</summary>
        public abstract Type RowType { get; }

        /// <summary>보관 중인 행 수.</summary>
        public abstract int Count { get; }

        /// <summary>
        /// 파싱된 시트로 내용을 통째로 교체한다. 도중에 예외가 나면 기존 데이터는 그대로 남는다.
        /// </summary>
        public abstract void LoadFrom(S3TextTable text);

        /// <summary>
        /// CSV 본문에서 바로 읽어들인다.
        /// 읽기 시작 좌표는 에셋에 구워두지 않고 <b>배포 번들이 함께 나른다</b> — 원본이 시트 설정이므로 거기서 곧장 온다.
        /// </summary>
        public void LoadFromCsv(string csv, S3Layout layout) => LoadFrom(S3TextTable.Parse(csv, TableName, layout));
    }
}
