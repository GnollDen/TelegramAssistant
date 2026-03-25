namespace TgAssistant.Web.Read;

public class WebReviewBoardModel
{
    public List<WebReviewCardModel> Cards { get; set; } = [];
}

public class WebReviewCardModel
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string SuggestedInterpretation { get; set; } = string.Empty;
    public string LinkedContext { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public bool CanEdit { get; set; }
}

public class WebReviewActionRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // confirm|reject|defer
    public string Actor { get; set; } = "web";
    public string? Reason { get; set; }
}

public class WebPeriodEditRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public Guid PeriodId { get; set; }
    public string? Label { get; set; }
    public string? Summary { get; set; }
    public short? ReviewPriority { get; set; }
    public bool? IsOpen { get; set; }
    public DateTime? EndAt { get; set; }
    public string Actor { get; set; } = "web";
    public string? Reason { get; set; }
}

public class WebReviewActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; }
}
