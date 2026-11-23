using EasyCon.Core.Script;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Core.Tests;

public partial class ScriptServiceTests
{
    [Test]
    public async Task BuildAsync_UsesCurrentEditorSnapshotAndMatchesCliExport()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("editor.ecs", "BREAK\n");
        const string editorText = "RETURN 42\n";

        Assert.That(await service.CompileAsync(editorText, path), Is.True);
        byte[] guiBytes = await service.BuildAsync(stripDebug: true);
        EcxExportResult cliEquivalent = EcxExportService.Export(editorText, path, [], stripDebug: true);

        Assert.Multiple(() =>
        {
            Assert.That(cliEquivalent.Success, Is.True, string.Join("; ", cliEquivalent.Diagnostics));
            Assert.That(guiBytes, Is.EqualTo(cliEquivalent.Bytes));
            Assert.That(log.Snapshot(), Does.Not.Contain("v1 Assemble"));
            Assert.That(log.Snapshot(), Does.Contain("ECX 导出完成: " + guiBytes.Length + " 字节"));
        });
    }

    [Test]
    public async Task BuildAsync_RejectsVisionAfterSuccessfulPcCompilation()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());

        Assert.That(await service.CompileAsync("RETURN OCR_INIT(\"eng\")\n", null), Is.True);
        Assert.That(await service.BuildAsync(stripDebug: true), Is.Empty);
        Assert.That(log.Snapshot().Any(message => message.Contains("无法生成单片机离线字节码", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public async Task BuildAsync_AfterCompileFailureDoesNotReusePreviousSnapshot()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());

        Assert.That(await service.CompileAsync("RETURN 42\n", null), Is.True);
        Assert.That(await service.CompileAsync("BREAK\n", null), Is.False);

        byte[] bytes = await service.BuildAsync(stripDebug: true);

        Assert.That(bytes, Is.Empty);
        Assert.That(log.Snapshot(), Does.Contain("ECX 导出失败: 没有可导出的已编译脚本"));
    }

    [Test]
    public async Task BuildAsync_RunCompilationDoesNotReplaceEditorSnapshot()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        const string editorText = "RETURN 42\n";
        const string runText = "RETURN 99\n";

        Assert.That(await service.CompileAsync(editorText, null), Is.True);
        service.RunFromContent(runText);
        await WaitUntilAsync(() => log.Snapshot().Contains("===开始运行脚本==="), "运行脚本应开始编译");
        await WaitUntilAsync(() => !service.IsRunning, "运行脚本应结束");

        byte[] guiBytes = await service.BuildAsync(stripDebug: true);
        EcxExportResult editorExport = EcxExportService.Export(editorText, null, [], stripDebug: true);
        EcxExportResult runExport = EcxExportService.Export(runText, null, [], stripDebug: true);

        Assert.Multiple(() =>
        {
            Assert.That(editorExport.Success, Is.True, string.Join("\n", editorExport.Diagnostics));
            Assert.That(runExport.Success, Is.True, string.Join("\n", runExport.Diagnostics));
            Assert.That(guiBytes, Is.EqualTo(editorExport.Bytes));
            Assert.That(guiBytes, Is.Not.EqualTo(runExport.Bytes));
        });
    }

    [Test]
    public async Task BuildAsync_FailedRunCompilationDoesNotClearEditorSnapshot()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        const string editorText = "RETURN 42\n";

        Assert.That(await service.CompileAsync(editorText, null), Is.True);
        service.RunFromContent("BREAK\n");
        await WaitUntilAsync(() => log.Snapshot().Contains("===开始运行脚本==="), "运行脚本应开始编译");
        await WaitUntilAsync(() => !service.IsRunning, "失败的运行脚本应结束");

        byte[] guiBytes = await service.BuildAsync(stripDebug: true);
        EcxExportResult editorExport = EcxExportService.Export(editorText, null, [], stripDebug: true);

        Assert.That(editorExport.Success, Is.True, string.Join("\n", editorExport.Diagnostics));
        Assert.That(guiBytes, Is.EqualTo(editorExport.Bytes));
    }
}