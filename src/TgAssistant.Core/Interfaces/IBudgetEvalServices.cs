using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IBudgetGuardrailService
{
    Task<BudgetPathDecision> EvaluatePathAsync(BudgetPathCheckRequest request, CancellationToken ct = default);
    Task<BudgetPathDecision> RegisterQuotaBlockedAsync(
        string pathKey,
        string modality,
        string reason,
        bool isImportScope,
        bool isOptionalPath,
        CancellationToken ct = default);
    Task<List<BudgetOperationalState>> GetOperationalStatesAsync(CancellationToken ct = default);
}

public interface IEvalHarnessService
{
    Task<EvalRunResult> RunAsync(EvalRunRequest request, CancellationToken ct = default);
}
