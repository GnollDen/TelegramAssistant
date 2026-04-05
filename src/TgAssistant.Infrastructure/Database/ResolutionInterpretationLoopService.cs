using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

// Compatibility wrapper while ResolutionInterpretationLoopV1 becomes the active bounded implementation.
public sealed class ResolutionInterpretationLoopService : IResolutionInterpretationLoopService
{
    private readonly ResolutionInterpretationLoopV1Service _inner;

    public ResolutionInterpretationLoopService(ResolutionInterpretationLoopV1Service inner)
    {
        _inner = inner;
    }

    public Task<ResolutionInterpretationLoopResult> InterpretAsync(
        ResolutionInterpretationLoopRequest request,
        CancellationToken ct = default)
        => _inner.InterpretAsync(request, ct);
}
