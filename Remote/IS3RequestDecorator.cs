using UnityEngine.Networking;

namespace KwonStudio.S3
{
    /// <summary>
    /// 런타임 요청에 서명·헤더·쿠키를 얹는다.
    /// 지금 배포는 공개 읽기라 아무도 구현하지 않지만, 서명 URL 이나 커스텀 헤더 인증이 확정되면
    /// 이 인터페이스만 구현해 <see cref="S3Remote.RequestDecorator"/>에 꽂으면 된다.
    /// 꽂는 시점은 반드시 첫 <see cref="S3Remote.RefreshAsync"/> 이전이어야 한다 (예: S3Loader.Awake).
    /// </summary>
    public interface IS3RequestDecorator
    {
        /// <summary>보내기 직전에 불린다. 서명 URL 이면 <c>request.url</c> 을, 헤더 방식이면 SetRequestHeader 를 쓴다.</summary>
        void Decorate(UnityWebRequest request);
    }
}
