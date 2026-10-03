using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Sportive.API.Models;

// ── Channel Types ──────────────────────────────────────────────────────────────
public enum InternalChatChannelType
{
    General = 0,    // غرفة عامة للكل
    Department = 1, // غرفة إدارة محددة
    Direct = 2,     // رسائل مباشرة بين موظفين
    Group = 3       // مجموعة مخصصة
}

// ── Channel ────────────────────────────────────────────────────────────────────
public class InternalChatChannel
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    public InternalChatChannelType Type { get; set; } = InternalChatChannelType.General;

    /// <summary>For Direct channels: "userId1_userId2" sorted alphabetically</summary>
    [MaxLength(200)]
    public string? DirectKey { get; set; }

    /// <summary>Emoji or department icon</summary>
    [MaxLength(10)]
    public string? Icon { get; set; }

    public bool IsArchived { get; set; } = false;

    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<InternalChatMessage> Messages { get; set; } = new();
    public List<InternalChatMember> Members { get; set; } = new();
}

// ── Message ────────────────────────────────────────────────────────────────────
public class InternalChatMessage
{
    [Key]
    public int Id { get; set; }

    public int ChannelId { get; set; }
    public InternalChatChannel Channel { get; set; } = null!;

    [Required]
    public string SenderId { get; set; } = "";

    [MaxLength(200)]
    public string SenderName { get; set; } = "";

    [MaxLength(500)]
    public string? SenderAvatarUrl { get; set; }

    [Required]
    public string Text { get; set; } = "";

    /// <summary>For replies: ID of the message being replied to</summary>
    public int? ReplyToMessageId { get; set; }
    public InternalChatMessage? ReplyToMessage { get; set; }

    /// <summary>@mention: comma-separated user IDs</summary>
    [MaxLength(2000)]
    public string? MentionedUserIds { get; set; }

    /// <summary>Linked entity type: "Order", "Customer", etc.</summary>
    [MaxLength(50)]
    public string? LinkedEntityType { get; set; }

    public int? LinkedEntityId { get; set; }

    [MaxLength(200)]
    public string? LinkedEntityRef { get; set; } // e.g. "POS-2609-0421"

    // Media attachment
    [MaxLength(2000)]
    public string? MediaUrl { get; set; }

    [MaxLength(50)]
    public string? MediaType { get; set; } // "image", "file", "video"

    [MaxLength(200)]
    public string? FileName { get; set; }

    public bool IsDeleted { get; set; } = false;
    public bool IsEdited { get; set; } = false;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    // Navigation
    public List<InternalChatReadReceipt> ReadReceipts { get; set; } = new();
    public List<InternalChatReaction> Reactions { get; set; } = new();
}

// ── Channel Member ─────────────────────────────────────────────────────────────
public class InternalChatMember
{
    [Key]
    public int Id { get; set; }

    public int ChannelId { get; set; }
    public InternalChatChannel Channel { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = "";

    [MaxLength(200)]
    public string UserName { get; set; } = "";

    public bool IsAdmin { get; set; } = false;

    /// <summary>Last time this user read messages in this channel</summary>
    public DateTime? LastReadAt { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Notification mute until this time (null = not muted)</summary>
    public DateTime? MutedUntil { get; set; }
}

// ── Read Receipt ───────────────────────────────────────────────────────────────
public class InternalChatReadReceipt
{
    [Key]
    public int Id { get; set; }

    public int MessageId { get; set; }
    public InternalChatMessage Message { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = "";

    [MaxLength(200)]
    public string UserName { get; set; } = "";

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

// ── Message Reaction ────────────────────────────────────────────────────────────
public class InternalChatReaction
{
    [Key]
    public int Id { get; set; }

    public int MessageId { get; set; }
    public InternalChatMessage Message { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = "";

    [MaxLength(200)]
    public string UserName { get; set; } = "";

    [Required, MaxLength(10)]
    public string Emoji { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
