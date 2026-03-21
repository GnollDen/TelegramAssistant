namespace TgAssistant.Web.Read;

public class InboxReadModel
{
    public string GroupFilter { get; set; } = "all";
    public string StatusFilter { get; set; } = "open";
    public string? PriorityFilter { get; set; }
    public bool? BlockingFilter { get; set; }
    public List<InboxItemReadModel> Blocking { get; set; } = [];
    public List<InboxItemReadModel> HighImpact { get; set; } = [];
    public List<InboxItemReadModel> EverythingElse { get; set; } = [];
    public int TotalVisible { get; set; }
}

public class InboxItemReadModel
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class HistoryReadModel
{
    public string? ObjectTypeFilter { get; set; }
    public string? ActionFilter { get; set; }
    public List<ActivityEventReadModel> Events { get; set; } = [];
}

public class ActivityEventReadModel
{
    public Guid Id { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TimestampLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class ObjectHistoryReadModel
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string ObjectSummary { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool? IsBlocking { get; set; }
    public List<ActivityEventReadModel> Events { get; set; } = [];
}

public class RecentChangesReadModel
{
    public List<ActivityEventReadModel> Items { get; set; } = [];
}
