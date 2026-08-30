using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// AWS 접속 정보를 입력받는 창. 값은 <see cref="AwsCredentials"/>를 통해 EditorPrefs 에만 저장되므로
    /// 리포에는 아무 파일도 생기지 않는다.
    /// </summary>
    public class AwsSettingsWindow : EditorWindow
    {
        private const string HelpText =
            "여기 넣는 키는 업로드 전용 IAM 사용자여야 한다.\n" +
            "필요한 권한은 대상 버킷에 대한 s3:PutObject 와 s3:ListBucket 둘뿐이고, 삭제 권한은 주지 않는다.\n" +
            "값은 이 PC 에만 저장된다(EditorPrefs).";

        private string _accessKeyId;
        private string _secretAccessKey;
        private string _region;
        private string _testBucket;
        private string _liveBucket;

        private S3UploadTarget _connectionTarget = S3UploadTarget.Test;
        private string _status;
        private bool _isBusy;

        /// <summary>임포터 창의 [설정] 버튼이 부른다.</summary>
        public static void Open()
        {
            var window = GetWindow<AwsSettingsWindow>(true, "AWS 설정");
            window.minSize = new Vector2(460f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            _accessKeyId = AwsCredentials.AccessKeyId;
            _secretAccessKey = AwsCredentials.SecretAccessKey;
            _region = AwsCredentials.Region;
            _testBucket = AwsCredentials.TestBucket;
            _liveBucket = AwsCredentials.LiveBucket;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(HelpText, MessageType.Info);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_isBusy))
            {
                EditorGUI.BeginChangeCheck();

                _accessKeyId = EditorGUILayout.TextField("Access Key ID", _accessKeyId);
                _secretAccessKey = EditorGUILayout.PasswordField("Secret Access Key", _secretAccessKey);
                _region = EditorGUILayout.TextField("리전", _region);

                EditorGUILayout.Space();
                _testBucket = EditorGUILayout.TextField("테섭 버킷", _testBucket);
                _liveBucket = EditorGUILayout.TextField("라이브 버킷", _liveBucket);

                if (EditorGUI.EndChangeCheck()) Save();

                DrawBucketNameWarning();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("업로드 경로", $"{S3Config.RemotePrefix}/{S3Config.BundleFileName}");
                EditorGUILayout.LabelField("런타임 읽기", S3Config.BuildBundleUrl());

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    _connectionTarget = (S3UploadTarget)EditorGUILayout.EnumPopup(_connectionTarget, GUILayout.Width(90f));

                    if (GUILayout.Button("연결 테스트"))
                    {
                        TestConnectionAsync().Forget();
                    }

                    if (GUILayout.Button("저장값 지우기", GUILayout.Width(110f)))
                    {
                        ClearAll();
                    }
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, _status.StartsWith("실패") ? MessageType.Error : MessageType.Info);
            }
        }

        private void DrawBucketNameWarning()
        {
            if (ContainsDot(_testBucket) || ContainsDot(_liveBucket))
            {
                EditorGUILayout.HelpBox(
                    "버킷 이름에 점(.)이 있으면 https 인증서가 맞지 않아 연결이 실패한다. 점 없는 이름을 쓸 것.",
                    MessageType.Warning);
            }
        }

        private static bool ContainsDot(string bucket) =>
            !string.IsNullOrEmpty(bucket) && bucket.Contains(".");

        private void Save()
        {
            AwsCredentials.AccessKeyId = _accessKeyId;
            AwsCredentials.SecretAccessKey = _secretAccessKey;
            AwsCredentials.Region = _region;
            AwsCredentials.TestBucket = _testBucket;
            AwsCredentials.LiveBucket = _liveBucket;
        }

        private void ClearAll()
        {
            AwsCredentials.Clear();
            OnEnable();
            _status = "저장된 값을 지웠습니다.";
        }

        private async UniTaskVoid TestConnectionAsync()
        {
            var invalid = AwsCredentials.Validate(_connectionTarget);
            if (invalid != null)
            {
                _status = "실패: " + invalid;
                Repaint();
                return;
            }

            _isBusy = true;
            _status = "확인 중...";
            Repaint();

            var bucket = AwsCredentials.BucketFor(_connectionTarget);
            var result = await AwsS3Client.ListObjectsAsync(bucket, AwsCredentials.Region, S3Config.RemotePrefix);

            _isBusy = false;

            if (result.IsSuccess)
            {
                _status = $"연결 성공. {bucket} 의 {S3Config.RemotePrefix} 아래에 객체 {result.Value.Count}개가 있습니다.";
            }
            else
            {
                _status = "실패: " + result.Error;
            }

            Repaint();
        }
    }
}
