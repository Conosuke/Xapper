using System.Runtime.InteropServices;
using Xapper.Injector;
using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 파일의 export 테이블에서 함수 RVA 를 읽는 규칙을 검증한다. 64비트 서버가 32비트 .NET Framework 앱에 주입할 때
/// LoadLibraryW 와 ExecuteInDefaultAppDomain 의 주소를 이 서버에 DLL 을 올리지 않고 구하는 유일한 길이다.
/// </summary>
public class PeExportsTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [Fact]
    public void Find_AgreesWithTheLoaderForThisProcessKernel32()
    {
        // 같은 비트 수의 kernel32 는 이 프로세스에도 올라와 있으므로, 로더가 준 주소와 베이스 + RVA 가 같아야 한다.
        var kernel32 = GetModuleHandleW("kernel32.dll");
        var loaderAddress = GetProcAddress(kernel32, "GetCurrentProcessId");

        var export = PeExports.Find(Path.Combine(Environment.SystemDirectory, "kernel32.dll"), "GetCurrentProcessId");

        Assert.NotNull(export);
        Assert.Null(export.Value.Forwarder);
        Assert.Equal((long)loaderAddress, (long)kernel32 + export.Value.Rva);
    }

    [Fact]
    public void Find_ReadsA32BitDllFromA64BitProcess()
    {
        // 이 테스트가 막는 회귀: 64비트 프로세스는 32비트 DLL 을 LoadLibrary 로 올릴 수 없어 예전 방식이 실패했다.
        var wowKernel32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "kernel32.dll");
        if (!File.Exists(wowKernel32))
            return;

        var export = PeExports.Find(wowKernel32, "LoadLibraryW");

        Assert.NotNull(export);
        Assert.True(export.Value.Rva != 0 || export.Value.Forwarder is not null);
    }

    [Fact]
    public void Find_LocatesTheGenericInjectorEntryForEveryArchitecture()
    {
        var extractor = new PayloadExtractor();
        extractor.ExtractAll();

        foreach (var arch in new[] { "x64", "x86", "ARM64" })
        {
            var dll = Path.Combine(extractor.GenericInjectorDir, $"Snoop.GenericInjector.{arch}.dll");

            var export = PeExports.Find(dll, "ExecuteInDefaultAppDomain");

            Assert.NotNull(export);
            Assert.Null(export.Value.Forwarder);
        }
    }

    [Fact]
    public void Find_ReturnsNullForAMissingExport()
    {
        Assert.Null(PeExports.Find(Path.Combine(Environment.SystemDirectory, "kernel32.dll"), "NoSuchExport_Xapper"));
    }
}
