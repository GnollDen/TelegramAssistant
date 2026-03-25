namespace TgAssistant.Web.Read;

public class SearchReadModel
{
    public string Query { get; set; } = string.Empty;
    public string? ObjectTypeFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? PriorityFilter { get; set; }
    public List<SearchResultReadModel> Results { get; set; } = [];
}

public class SearchResultReadModel
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Link { get; set; } = string.Empty;
}

public class SavedViewReadModel
{
    public string ViewKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SearchResultReadModel> Items { get; set; } = [];
}

public class DossierReadModel
{
    public string Summary { get; set; } = string.Empty;
    public List<DossierInsightReadModel> ObservedFacts { get; set; } = [];
    public List<DossierInsightReadModel> RelationshipRead { get; set; } = [];
    public List<DossierInsightReadModel> NotableEvents { get; set; } = [];
    public List<DossierInsightReadModel> LikelyInterpretation { get; set; } = [];
    public List<DossierInsightReadModel> Uncertainties { get; set; } = [];
    public List<DossierInsightReadModel> MissingInformation { get; set; } = [];
    public List<DossierInsightReadModel> PracticalInterpretation { get; set; } = [];

    public List<DossierItemReadModel> Confirmed { get; set; } = [];
    public List<DossierItemReadModel> Hypotheses { get; set; } = [];
    public List<DossierItemReadModel> Conflicts { get; set; } = [];
}

public class DossierInsightReadModel
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string SignalStrength { get; set; } = "weak";
    public string Evidence { get; set; } = string.Empty;
    public string? SourceObjectType { get; set; }
    public string? SourceObjectId { get; set; }
    public string Link { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class DossierItemReadModel
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ConfidenceLabel { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
