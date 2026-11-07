using System.Reflection;

[assembly: AssemblyVersion(SCPSLAlert.AppInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(SCPSLAlert.AppInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion(SCPSLAlert.AppInfo.Version)]
[assembly: AssemblyProduct(SCPSLAlert.AppInfo.Product)]
[assembly: AssemblyTitle(SCPSLAlert.AppInfo.Product + " // " + SCPSLAlert.AppInfo.Brand)]
[assembly: AssemblyDescription(SCPSLAlert.AppInfo.BrandFull)]

namespace SCPSLAlert
{
    /// <summary>
    /// 全软件唯一的产品名、品牌与版本来源。
    /// 界面、水印、导出、崩溃报告都从这里读，不要在各处再写死版本号。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>界面/导出显示的版本（三段式）。</summary>
        public const string Version = "1.0.0";

        /// <summary>程序集版本（四段式，Windows 文件属性用）。</summary>
        public const string AssemblyVersion = "1.0.0.0";

        /// <summary>软件正式名，exe、路径、水印都保留它。</summary>
        public const string Product = "SCPSLAlert";

        /// <summary>品牌短名。</summary>
        public const string Brand = "A.R.C.T";

        /// <summary>品牌全称。</summary>
        public const string BrandFull = "Automated Response & Cognitive Terminal";

        /// <summary>品牌并列署名：SCPSLAlert · A.R.C.T</summary>
        public const string Signature = Product + "  ·  " + Brand;
    }
}
