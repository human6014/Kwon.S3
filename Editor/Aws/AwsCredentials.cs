using UnityEditor;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>업로드 대상 환경. 테섭과 라이브는 별개 버킷·별개 CloudFront 배포다.</summary>
    public enum S3UploadTarget
    {
        Test,
        Live,
    }

    /// <summary>
    /// AWS 접속 정보. 전부 <see cref="EditorPrefs"/>(머신 로컬)에 저장한다.
    /// 리포에 파일이 생기지 않으므로 자격 증명이 실수로 커밋될 여지가 없다.
    /// 대신 평문으로 남으므로, 여기 넣는 키는 <b>해당 버킷에 올리고 목록을 보는 권한만</b> 가진 전용 IAM 사용자여야 한다.
    /// </summary>
    public static class AwsCredentials
    {
        private const string AccessKeyIdKey = "KwonStudio.S3.Aws.AccessKeyId";
        private const string SecretAccessKeyKey = "KwonStudio.S3.Aws.SecretAccessKey";
        private const string RegionKey = "KwonStudio.S3.Aws.Region";
        private const string TestBucketKey = "KwonStudio.S3.Aws.TestBucket";
        private const string LiveBucketKey = "KwonStudio.S3.Aws.LiveBucket";

        private const string DefaultRegion = "ap-northeast-2";

        public static string AccessKeyId
        {
            get => EditorPrefs.GetString(AccessKeyIdKey, string.Empty);
            set => EditorPrefs.SetString(AccessKeyIdKey, value ?? string.Empty);
        }

        public static string SecretAccessKey
        {
            get => EditorPrefs.GetString(SecretAccessKeyKey, string.Empty);
            set => EditorPrefs.SetString(SecretAccessKeyKey, value ?? string.Empty);
        }

        public static string Region
        {
            get => EditorPrefs.GetString(RegionKey, DefaultRegion);
            set => EditorPrefs.SetString(RegionKey, value ?? string.Empty);
        }

        public static string TestBucket
        {
            get => EditorPrefs.GetString(TestBucketKey, string.Empty);
            set => EditorPrefs.SetString(TestBucketKey, value ?? string.Empty);
        }

        public static string LiveBucket
        {
            get => EditorPrefs.GetString(LiveBucketKey, string.Empty);
            set => EditorPrefs.SetString(LiveBucketKey, value ?? string.Empty);
        }

        /// <summary>키와 리전이 채워져 있는지. 버킷은 대상별로 따로 확인한다.</summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AccessKeyId) &&
            !string.IsNullOrWhiteSpace(SecretAccessKey) &&
            !string.IsNullOrWhiteSpace(Region);

        public static string BucketFor(S3UploadTarget target) =>
            target == S3UploadTarget.Live ? LiveBucket : TestBucket;

        /// <summary>설정이 부족하면 사람이 읽을 수 있는 사유를, 충분하면 null 을 돌려준다.</summary>
        public static string Validate(S3UploadTarget target)
        {
            if (string.IsNullOrWhiteSpace(AccessKeyId)) return "Access Key ID 가 비어 있습니다.";
            if (string.IsNullOrWhiteSpace(SecretAccessKey)) return "Secret Access Key 가 비어 있습니다.";
            if (string.IsNullOrWhiteSpace(Region)) return "리전이 비어 있습니다.";

            if (string.IsNullOrWhiteSpace(BucketFor(target)))
            {
                var label = target == S3UploadTarget.Live ? "라이브" : "테섭";
                return $"{label} 버킷 이름이 비어 있습니다.";
            }

            return null;
        }

        /// <summary>저장된 값을 모두 지운다. 키를 교체하거나 공용 PC 에서 정리할 때 쓴다.</summary>
        public static void Clear()
        {
            EditorPrefs.DeleteKey(AccessKeyIdKey);
            EditorPrefs.DeleteKey(SecretAccessKeyKey);
            EditorPrefs.DeleteKey(RegionKey);
            EditorPrefs.DeleteKey(TestBucketKey);
            EditorPrefs.DeleteKey(LiveBucketKey);
        }
    }
}
