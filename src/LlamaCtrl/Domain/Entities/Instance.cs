using LlamaCtrl.Domain.Enums;

namespace LlamaCtrl.Domain.Entities;

public class Instance
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProfileId { get; set; }
    public int Port { get; set; }
    public int? Pid { get; set; }
    public InstanceStatus Status { get; set; } = InstanceStatus.Stopped;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Profile Profile { get; set; } = null!;
}
