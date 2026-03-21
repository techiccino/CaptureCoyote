using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IAnnotationRenderService
{
    byte[] Render(ScreenshotProject project, ExportOptions options);

    byte[] RenderToPng(ScreenshotProject project);
}
