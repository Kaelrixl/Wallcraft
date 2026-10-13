namespace Wallcraft;

internal static class Program
{
    internal static string PortableDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    [STAThread]
    private static int Main(string[] args)
    {
        // 打包校验专用入口：加载运行库，但不创建窗口或读写用户配置。
        if (args.Length == 1 && args[0] == "--verify-package")
        {
            var tool = Path.Combine(AppContext.BaseDirectory, "repkg-ng", "repkg-win-x64.exe");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                Application = AppContext.BaseDirectory,
                Configuration = PortableDirectory,
                Forms = typeof(Form).Assembly.Location,
                ToolExists = File.Exists(tool)
            }));
            return File.Exists(tool) ? 0 : 1;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
