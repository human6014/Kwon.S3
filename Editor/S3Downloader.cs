using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 에디터(플레이 모드가 아닌 상태)에서 시트를 받아온다.
    /// 이때는 플레이어 루프가 돌지 않아 UniTask의 기본 대기가 진행되지 않으므로
    /// <see cref="EditorApplication.update"/>로 직접 완료를 감시한다.
    /// 런타임 다운로드는 <see cref="S3Remote"/>를 쓴다.
    /// </summary>
    public static class S3Downloader
    {
        private const int TimeoutSeconds = 30;

        /// <summary>
        /// 연결이 끊겼을 때 더 걸어보는 횟수. 구글이 가끔 응답 없이 끊어서 임포트가 통째로 실패한다.
        /// 한 번 실패하면 사람이 창에서 다시 눌러야 하는 흐름이라, 여기서 조용히 다시 시도하는 편이 낫다.
        /// </summary>
        private const int RetryCount = 2;

        /// <param name="allowHtml">
        /// CSV를 받을 때 HTML이 오면 비공개 문서라는 뜻이라 오류로 처리한다.
        /// 탭 목록을 읽는 htmlview처럼 HTML이 정상인 경우에만 true를 넘긴다.
        /// </param>
        public static async UniTask<string> GetTextAsync(string url, bool allowHtml = false)
        {
            for (var attempt = 1; ; attempt++)
            {
                var (text, error, isTransient) = await SendAsync(url, allowHtml);
                if (error == null) return text;

                // 응답 코드 0은 서버가 답한 실패가 아니라 연결이 끊긴 것이라 다시 걸어볼 값어치가 있다.
                // 403·404 나 '비공개 문서라 HTML이 왔다'는 다시 해도 결과가 같으니 바로 던진다.
                if (!isTransient || attempt > RetryCount) throw new S3Exception(error);

                Debug.LogWarning($"[S3] 연결이 끊겨 다시 시도합니다. ({attempt}/{RetryCount})\nURL: {url}");
            }
        }

        private static UniTask<(string Text, string Error, bool IsTransient)> SendAsync(string url, bool allowHtml)
        {
            var source = new UniTaskCompletionSource<(string, string, bool)>();
            var request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;

            var operation = request.SendWebRequest();

            void OnEditorUpdate()
            {
                if (!operation.isDone) return;

                EditorApplication.update -= OnEditorUpdate;

                string text = null;
                string error = null;
                var isTransient = false;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    error = $"다운로드에 실패했습니다. (HTTP {request.responseCode}) {request.error}\nURL: {url}";
                    isTransient = request.responseCode == 0;
                }
                else
                {
                    text = request.downloadHandler.text;
                    if (!allowHtml && LooksLikeHtml(text))
                    {
                        error = "CSV 대신 HTML 페이지가 돌아왔습니다. 보통 문서가 비공개일 때 생깁니다.\n" +
                                "구글 시트에서 [공유] → '링크가 있는 모든 사용자'를 뷰어로 바꿔주세요.\n" +
                                $"URL: {url}";
                    }
                }

                request.Dispose();

                source.TrySetResult((text, error, isTransient));
            }

            EditorApplication.update += OnEditorUpdate;

            return source.Task;
        }

        private static bool LooksLikeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            var head = text.TrimStart();
            return head.StartsWith("<!DOCTYPE html", System.StringComparison.OrdinalIgnoreCase) ||
                   head.StartsWith("<html", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
