using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 문서에 어떤 탭들이 있는지 알아낸다.
    /// CSV export는 탭 하나만 주기 때문에, 등록되지 않은 탭은 원래 보이지 않는다.
    /// htmlview 페이지에는 모든 탭의 이름과 gid가 스크립트로 박혀 있어서 API 키 없이 목록을 얻을 수 있다.
    /// 파싱 자체는 <see cref="S3TabParser"/>가 한다.
    /// </summary>
    public static class S3SheetIndex
    {
        /// <summary>데이터 문서의 탭 목록.</summary>
        public static UniTask<List<S3TabInfo>> FetchAsync() => FetchAsync(S3Config.SpreadsheetId);

        /// <summary>지정한 문서의 탭 목록.</summary>
        public static async UniTask<List<S3TabInfo>> FetchAsync(string documentId)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                throw new S3Exception("문서 ID가 비어 있습니다. S3Config 의 상수를 확인하세요.");
            }

            var html = await S3Downloader.GetTextAsync(S3Config.HtmlViewUrl(documentId), true);
            return S3TabParser.Parse(html);
        }
    }
}
