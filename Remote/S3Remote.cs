using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace KwonStudio.S3
{
    /// <summary>테이블 한 개를 갱신한 결과.</summary>
    public enum S3RefreshResult
    {
        /// <summary>번들에서 찾아 반영했다.</summary>
        Downloaded,

        /// <summary>반영하지 못했다. 이 테이블은 비어 있는 상태로 남는다.</summary>
        Failed,
    }

    /// <summary>테이블 하나를 갱신한 뒤 남는 기록. 실패했을 때 사유를 화면에 보여주기 위해 들고 다닌다.</summary>
    public readonly struct S3RefreshOutcome
    {
        public S3RefreshOutcome(string tableName, S3RefreshResult result, string error)
        {
            TableName = tableName;
            Result = result;
            Error = error;
        }

        public string TableName { get; }

        public S3RefreshResult Result { get; }

        /// <summary>실패 사유. 성공했으면 null.</summary>
        public string Error { get; }

        public bool IsSuccess => Result == S3RefreshResult.Downloaded;
    }

    /// <summary>
    /// 배포된 데이터를 받아 테이블을 채운다.
    /// <para>
    /// <b>폴백이 없다.</b> 받지 못하면 그 테이블은 비어 있고, 게임은 진행하지 않는다.
    /// 데이터 출처를 배포본 하나로 고정해, 파이프라인이 죽었는데 옛 데이터로 조용히 도는 상황을 없애기 위해서다.
    /// 대신 네트워크 없이는 게임이 시작되지 않는다.
    /// </para>
    /// <para>
    /// 테이블 전부가 <b>번들 파일 하나</b>에 들어 있어 요청은 1회다. 일부만 받아진 상태가 애초에 생기지 않는다.
    /// </para>
    /// </summary>
    public static class S3Remote
    {
        /// <summary>기본 타임아웃(초). 모바일 회선을 감안해 넉넉히 잡았다.</summary>
        public const int DefaultTimeoutSeconds = 20;

        /// <summary>
        /// 요청에 인증을 얹고 싶을 때 꽂는다. null 이면 아무것도 하지 않는다.
        /// 첫 <see cref="RefreshFromBundleAsync"/> 호출 전에 세팅돼 있어야 한다.
        /// </summary>
        public static IS3RequestDecorator RequestDecorator { get; set; }

        /// <summary>
        /// 마지막 성공 응답이 알려준 서버 시각(UTC). 읽지 못했으면 null 이다.
        /// </summary>
        /// <remarks>
        /// <b>여기서는 노출만 하고 쓰지 않는다.</b> 이 어셈블리(KwonStudio.S3)는 게임을 모르는 잎이라
        /// 시각을 받아 무엇을 할지(시계 보정·진입 차단)는 아는 쪽인 <c>S3Loader</c> 가 정한다.
        /// <see cref="RequestDecorator"/> 와 같은 방향의 분리다.
        ///
        /// <c>file://</c> 응답에는 헤더가 없어 null 로 남는다 — 에디터 오프라인 모드가 그 경우다.
        /// </remarks>
        public static DateTime? LastServerUtc { get; private set; }

        /// <summary>
        /// 번들을 한 번 받아 등록된 테이블을 모두 채운다.
        /// 다운로드나 번들 해석이 실패하면 테이블을 건드리지 않고 전부 실패로 보고한다.
        /// </summary>
        public static async UniTask<S3LoadReport> RefreshFromBundleAsync(
            IReadOnlyList<S3TableBase> tables,
            int timeoutSeconds = DefaultTimeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            if (tables == null || tables.Count == 0)
            {
                return new S3LoadReport(0, 0, null, null);
            }

            string json;

            try
            {
                json = await DownloadTextAsync(S3Config.BuildBundleUrl(), timeoutSeconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return FailAll(tables, e.Message);
            }

            if (!S3Bundle.TryParse(json, out var bundle, out var parseError))
            {
                return FailAll(tables, parseError);
            }

            var outcomes = new List<S3RefreshOutcome>(tables.Count);

            foreach (var table in tables)
            {
                if (table == null) continue;

                outcomes.Add(Apply(table, bundle));
            }

            return Summarize(outcomes);
        }

        /// <summary>본문을 받아온다. HTTP 오류만 여기서 걸러내고, 내용 검증은 부르는 쪽이 한다.</summary>
        public static async UniTask<string> DownloadTextAsync(
            string url,
            int timeoutSeconds = DefaultTimeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = timeoutSeconds;
                RequestDecorator?.Decorate(request);

                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new S3Exception($"다운로드 실패 (HTTP {request.responseCode}) {request.error}");
                }

                LastServerUtc = ReadServerUtc(request);

                return request.downloadHandler.text;
            }
        }

        /// <summary>
        /// 서버 시각만 물어본다. 본문을 받지 않는 HEAD 라 번들 크기와 무관하게 가볍다.
        /// </summary>
        /// <returns>읽어낸 서버 시각(UTC). 요청이 실패했거나 헤더가 없으면 null.</returns>
        /// <remarks>
        /// 앱이 백그라운드에 다녀온 뒤 시계를 다시 맞추는 데 쓴다. 실패를 예외로 올리지 않는 이유는
        /// 부르는 쪽이 "못 받았다"를 정상적인 분기로 다루기 때문이다.
        /// </remarks>
        public static async UniTask<DateTime?> FetchServerUtcAsync(
            int timeoutSeconds = DefaultTimeoutSeconds, CancellationToken cancellationToken = default)
        {
            using (var request = UnityWebRequest.Head(S3Config.BuildBundleUrl()))
            {
                request.timeout = timeoutSeconds;
                RequestDecorator?.Decorate(request);

                try
                {
                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // 네트워크가 끊긴 정상적인 실패다. 판단은 부르는 쪽이 한다.
                    Debug.LogWarning($"[S3] 서버 시각을 받지 못했습니다: {e.Message}");
                    return null;
                }

                if (request.result != UnityWebRequest.Result.Success) return null;

                var serverUtc = ReadServerUtc(request);
                if (serverUtc.HasValue) LastServerUtc = serverUtc;

                return serverUtc;
            }
        }

        /// <summary>
        /// 응답 헤더에서 서버 시각을 읽는다. 없거나 해석할 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// <b><c>Age</c> 를 반드시 더한다.</b> 번들은 정적 파일이라 CDN 엣지 캐시에 거의 항상 hit 하는데,
        /// 캐시는 응답을 재사용할 때 <c>Date</c> 를 <b>원본이 만들어진 시각 그대로 두고</b>
        /// 그 뒤 흐른 초를 <c>Age</c> 로 따로 붙인다. 이걸 빠뜨리면 캐시 TTL 만큼(수 시간) 과거를
        /// 현재 시각으로 믿게 되고, 증상이 "시계가 조금 느림"처럼 보여 원인을 찾기 어렵다.
        ///
        /// <c>Date</c> 는 규격상 언제나 GMT(=UTC0)다. 초 단위까지만 있고 편도 지연만큼 과거이므로
        /// 오차는 1~2초인데, 일일 초기화 판정에는 충분해서 왕복 시간 보정은 하지 않는다.
        /// </remarks>
        public static DateTime? ReadServerUtc(UnityWebRequest request)
        {
            var date = request?.GetResponseHeader("Date");
            if (string.IsNullOrEmpty(date)) return null;

            // AssumeUniversal 이 없으면 "GMT" 를 떼고 기기 시간대로 해석해 시차만큼 어긋난다.
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            {
                Debug.LogWarning($"[S3] Date 헤더를 해석하지 못했습니다: {date}");
                return null;
            }

            var age = request.GetResponseHeader("Age");
            if (!string.IsNullOrEmpty(age) &&
                long.TryParse(age, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ageSeconds) &&
                ageSeconds > 0L)
            {
                utc = utc.AddSeconds(ageSeconds);
            }

            return utc;
        }

        private static S3RefreshOutcome Apply(S3TableBase table, S3Bundle bundle)
        {
            var entry = bundle.Find(table.TableName);

            if (entry == null)
            {
                const string reason = "번들에 이 테이블이 없습니다. 시트 목록과 업로드 상태를 확인하세요.";
                Debug.LogError($"[S3] {table.TableName} 갱신 실패: {reason}");
                return new S3RefreshOutcome(table.TableName, S3RefreshResult.Failed, reason);
            }

            try
            {
                table.LoadFromCsv(entry.csv, entry.ToLayout());
                return new S3RefreshOutcome(table.TableName, S3RefreshResult.Downloaded, null);
            }
            catch (Exception e)
            {
                // 게임이 진행되지 않는 상황이라 릴리즈에서도 보여야 한다.
                Debug.LogError($"[S3] {table.TableName} 갱신 실패: {e.Message}");
                return new S3RefreshOutcome(table.TableName, S3RefreshResult.Failed, e.Message);
            }
        }

        /// <summary>번들 자체를 못 받았을 때. 개별 테이블을 따져볼 것도 없이 전부 실패다.</summary>
        private static S3LoadReport FailAll(IReadOnlyList<S3TableBase> tables, string error)
        {
            Debug.LogError($"[S3] 번들을 받지 못했습니다: {error}");
            return new S3LoadReport(tables.Count, tables.Count, error, null);
        }

        private static S3LoadReport Summarize(List<S3RefreshOutcome> outcomes)
        {
            var failed = 0;
            string firstError = null;
            var failedNames = new StringBuilder();

            foreach (var outcome in outcomes)
            {
                if (outcome.IsSuccess) continue;

                failed++;
                firstError ??= outcome.Error;

                // 이름을 전부 늘어놓으면 화면을 넘기므로 앞의 몇 개만 보여준다.
                if (failed <= 5)
                {
                    if (failedNames.Length > 0) failedNames.Append(", ");
                    failedNames.Append(outcome.TableName);
                }
                else if (failed == 6)
                {
                    failedNames.Append(" 외");
                }
            }

            return new S3LoadReport(outcomes.Count, failed, firstError, failedNames.ToString());
        }
    }
}
