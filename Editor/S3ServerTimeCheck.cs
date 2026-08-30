using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 배포 서버가 알려주는 시각을 눈으로 확인하는 점검 도구.
    /// </summary>
    /// <remarks>
    /// 테스트 어셈블리가 아직 없어서, <see cref="S3Remote.ReadServerUtc"/> 가 실제 응답에 대해
    /// 제대로 도는지 확인할 수 있는 유일한 수단이다. 헤더 원문을 함께 찍는 이유는
    /// <b><c>Age</c> 보정이 실제로 걸리는지</b>를 보기 위해서다 — 번들은 캐시에 hit 하는 정적 파일이라
    /// <c>Age</c> 를 빠뜨리면 몇 시간 과거를 현재로 믿게 되는데, 증상이 "시계가 조금 느림"처럼 보인다.
    /// </remarks>
    public static class S3ServerTimeCheck
    {
        /// <summary>임포터 창의 [서버 시각 확인] 버튼이 부른다.</summary>
        public static void Check() => CheckAsync().Forget();

        private static async UniTaskVoid CheckAsync()
        {
            var url = S3Config.BuildBundleUrl();

            using (var request = UnityWebRequest.Head(url))
            {
                request.timeout = S3Config.TimeoutSeconds;

                // 일부러 데코레이터를 꽂지 않는다. 오프라인 모드로 file:// 을 보면 헤더가 없어 확인이 안 된다.
                try
                {
                    await request.SendWebRequest();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[S3] 서버 시각 확인 실패: {e.Message}\n{url}");
                    return;
                }

                var date = request.GetResponseHeader("Date");
                var age = request.GetResponseHeader("Age");
                var serverUtc = S3Remote.ReadServerUtc(request);

                if (!serverUtc.HasValue)
                {
                    Debug.LogError($"[S3] 응답에서 시각을 읽지 못했습니다. Date=\"{date}\" Age=\"{age}\"\n{url}");
                    return;
                }

                var deviceUtc = DateTime.UtcNow;
                var drift = (serverUtc.Value - deviceUtc).TotalSeconds;

                Debug.Log(
                    $"[S3] 서버 시각 확인\n" +
                    $"  URL       {url}\n" +
                    $"  Date      {date}\n" +
                    $"  Age       {(string.IsNullOrEmpty(age) ? "(없음 - 캐시 미스)" : age + "초")}\n" +
                    $"  서버 시각  {serverUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\n" +
                    $"  기기 시계  {deviceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\n" +
                    $"  차이      {drift:0.#}초");

                // 캐시 hit 인데 Age 를 못 더했다면 서버 시각이 과거로 크게 밀린다. 그 상태를 눈에 띄게 알린다.
                if (drift < -60d)
                {
                    Debug.LogWarning(
                        "[S3] 서버 시각이 기기 시계보다 1분 이상 과거입니다. Age 보정이나 기기 시계를 확인하세요.");
                }
            }
        }
    }
}
