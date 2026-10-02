using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/internal-chat")]
[Authorize]
public class InternalChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public InternalChatController(AppDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string UserName => User.FindFirstValue("FullName") ?? User.Identity?.Name ?? "موظف";

    // ── GET /api/internal-chat/channels ─────────────────────────────────────────
    /// <summary>Get all channels the current user is a member of, with unread count</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetMyChannels()
    {
        var userId = UserId;

        // Ensure General channel exists and user is a member
        await EnsureUserInGeneralChannelAsync(userId);

        var myMemberships = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId)
            .ToListAsync();

        var channels = await _db.InternalChatChannels
            .AsNoTracking()
            .Include(c => c.Members)
            .Include(c => c.Messages.OrderByDescending(msg => msg.SentAt).Take(1))
            .Where(c => myMemberships.Contains(c.Id) && !c.IsArchived)
            .OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt)
            .ToListAsync();

        // Get unread counts
        var memberLastRead = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && myMemberships.Contains(m.ChannelId))
            .ToDictionaryAsync(m => m.ChannelId, m => m.LastReadAt);

        var result = channels.Select(c =>
        {
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var lastReadAt = memberLastRead.TryGetValue(c.Id, out var lr) ? lr : null;
            var unread = c.Messages.Count(m => m.SenderId != userId && (lastReadAt == null || m.SentAt > lastReadAt));

            // For Direct channels, get the other user's name
            string displayName = c.Name;
            string? otherUserId = null;
            if (c.Type == InternalChatChannelType.Direct)
            {
                var other = c.Members.FirstOrDefault(m => m.UserId != userId);
                displayName = other?.UserName ?? c.Name;
                otherUserId = other?.UserId;
            }

            return new
            {
                id = c.Id,
                name = displayName,
                originalName = c.Name,
                description = c.Description,
                type = c.Type.ToString(),
                icon = c.Icon,
                unreadCount = unread,
                membersCount = c.Members.Count,
                otherUserId = otherUserId,
                lastMessage = lastMsg == null ? null : new
                {
                    id = lastMsg.Id,
                    text = lastMsg.IsDeleted ? "🗑️ تم حذف هذه الرسالة" : lastMsg.Text,
                    senderName = lastMsg.SenderName,
                    senderId = lastMsg.SenderId,
                    sentAt = lastMsg.SentAt,
                    mediaType = lastMsg.MediaType
                }
            };
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/internal-chat/channels/{id}/messages ─────────────────────────
    [HttpGet("channels/{channelId}/messages")]
    public async Task<IActionResult> GetMessages(int channelId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = UserId;
        var isMember = await _db.InternalChatMembers
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (!isMember) return Forbid();

        var total = await _db.InternalChatMessages.CountAsync(m => m.ChannelId == channelId);

        var messages = await _db.InternalChatMessages
            .AsNoTracking()
            .Include(m => m.ReplyToMessage)
            .Include(m => m.ReadReceipts)
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        messages.Reverse(); // Ascending for display

        // Mark channel as read for this user
        var membership = await _db.InternalChatMembers
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (membership != null)
        {
            membership.LastReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var result = messages.Select(m => MapMessage(m, userId)).ToList();

        return Ok(new { total, page, pageSize, messages = result });
    }

    // ── POST /api/internal-chat/channels/{id}/messages ────────────────────────
    [HttpPost("channels/{channelId}/messages")]
    public async Task<IActionResult> SendMessage(int channelId, [FromBody] SendMessageRequest req)
    {
        var userId = UserId;
        var userName = UserName;

        var isMember = await _db.InternalChatMembers
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (!isMember) return Forbid();

        var msg = new InternalChatMessage
        {
            ChannelId = channelId,
            SenderId = userId,
            SenderName = userName,
            Text = req.Text?.Trim() ?? "",
            ReplyToMessageId = req.ReplyToMessageId,
            MentionedUserIds = req.MentionedUserIds,
            LinkedEntityType = req.LinkedEntityType,
            LinkedEntityId = req.LinkedEntityId,
            LinkedEntityRef = req.LinkedEntityRef,
            MediaUrl = req.MediaUrl,
            MediaType = req.MediaType,
            FileName = req.FileName,
            SentAt = DateTime.UtcNow
        };

        _db.InternalChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Mark as read for sender
        var membership = await _db.InternalChatMembers
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (membership != null)
        {
            membership.LastReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // Reload with reply info
        await _db.Entry(msg).Reference(m => m.ReplyToMessage).LoadAsync();

        var mapped = MapMessage(msg, userId);

        // Broadcast via SignalR to all channel members
        await _hub.Clients.All.SendAsync("ReceiveInternalChatMessage", new
        {
            channelId = channelId,
            message = mapped
        });

        // Send mention notifications
        if (!string.IsNullOrWhiteSpace(req.MentionedUserIds))
        {
            var mentionedIds = req.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var mentionedId in mentionedIds.Where(id => id != userId))
            {
                await _hub.Clients.Group($"user_{mentionedId}").SendAsync("ReceiveInternalChatMention", new
                {
                    channelId,
                    messageId = msg.Id,
                    fromName = userName,
                    preview = msg.Text.Length > 80 ? msg.Text[..80] + "..." : msg.Text
                });
            }
        }

        return Ok(mapped);
    }

    // ── GET /api/internal-chat/channels ─────────────────────────────────────────
    /// <summary>Get all available channels + users for DM</summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable()
    {
        var userId = UserId;

        // All public channels (General + Department)
        var publicChannels = await _db.InternalChatChannels
            .AsNoTracking()
            .Include(c => c.Members)
            .Where(c => !c.IsArchived && c.Type != InternalChatChannelType.Direct)
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Name)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                type = c.Type.ToString(),
                icon = c.Icon,
                description = c.Description,
                membersCount = c.Members.Count,
                isMember = c.Members.Any(m => m.UserId == userId)
            })
            .ToListAsync();

        var staffIds = await GetStaffUserIdsAsync();

        // All staff users for DMs (internal employees only)
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id != userId && staffIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                id = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? "موظف") : u.FullName,
                avatarUrl = u.ProfileImageUrl
            })
            .ToListAsync();

        return Ok(new { channels = publicChannels, users });
    }

    // ── POST /api/internal-chat/channels ─────────────────────────────────────────
    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateChannelRequest req)
    {
        var userId = UserId;
        var userName = UserName;

        // DM: check or create
        if (req.Type == "Direct" && !string.IsNullOrEmpty(req.TargetUserId))
        {
            var ids = new[] { userId, req.TargetUserId }.OrderBy(x => x).ToArray();
            var directKey = $"{ids[0]}_{ids[1]}";
            var existing = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == directKey);
            if (existing != null)
                return Ok(new { id = existing.Id, alreadyExists = true });

            // Get target user name
            var targetUser = await _db.Users.FindAsync(req.TargetUserId);
            var channel = new InternalChatChannel
            {
                Name = $"DM_{directKey}",
                Type = InternalChatChannelType.Direct,
                DirectKey = directKey,
                CreatedByUserId = userId,
                Members = new List<InternalChatMember>
                {
                    new() { UserId = userId, UserName = userName },
                    new() { UserId = req.TargetUserId, UserName = targetUser?.FullName ?? req.TargetUserId }
                }
            };
            _db.InternalChatChannels.Add(channel);
            await _db.SaveChangesAsync();
            return Ok(new { id = channel.Id, alreadyExists = false });
        }

        // Regular channel
        var newChannel = new InternalChatChannel
        {
            Name = req.Name?.Trim() ?? "قناة جديدة",
            Description = req.Description,
            Type = Enum.TryParse<InternalChatChannelType>(req.Type, out var ct) ? ct : InternalChatChannelType.Department,
            Icon = req.Icon,
            CreatedByUserId = userId,
            Members = new List<InternalChatMember>
            {
                new() { UserId = userId, UserName = userName, IsAdmin = true }
            }
        };

        if (req.MemberIds != null)
        {
            var members = await _db.Users
                .Where(u => req.MemberIds.Contains(u.Id))
                .ToListAsync();
            foreach (var m in members.Where(m => m.Id != userId))
                newChannel.Members.Add(new InternalChatMember { UserId = m.Id, UserName = m.FullName ?? m.UserName ?? "" });
        }

        _db.InternalChatChannels.Add(newChannel);
        await _db.SaveChangesAsync();

        // Notify new members
        await _hub.Clients.All.SendAsync("InternalChatChannelCreated", new
        {
            channelId = newChannel.Id,
            name = newChannel.Name,
            type = newChannel.Type.ToString()
        });

        return Ok(new { id = newChannel.Id });
    }

    // ── POST /api/internal-chat/channels/{id}/join ────────────────────────────
    [HttpPost("channels/{channelId}/join")]
    public async Task<IActionResult> JoinChannel(int channelId)
    {
        var userId = UserId;
        var already = await _db.InternalChatMembers
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (already) return Ok(new { success = true });

        var channel = await _db.InternalChatChannels.FindAsync(channelId);
        if (channel == null || channel.Type == InternalChatChannelType.Direct) return NotFound();

        _db.InternalChatMembers.Add(new InternalChatMember
        {
            ChannelId = channelId,
            UserId = userId,
            UserName = UserName
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── POST /api/internal-chat/messages/{id}/read ────────────────────────────
    [HttpPost("messages/{messageId}/read")]
    public async Task<IActionResult> MarkRead(int messageId)
    {
        var userId = UserId;
        var msg = await _db.InternalChatMessages.FindAsync(messageId);
        if (msg == null) return NotFound();

        var already = await _db.InternalChatReadReceipts
            .AnyAsync(r => r.MessageId == messageId && r.UserId == userId);
        if (already) return Ok();

        _db.InternalChatReadReceipts.Add(new InternalChatReadReceipt
        {
            MessageId = messageId,
            UserId = userId,
            UserName = UserName
        });

        // Update channel last read
        var membership = await _db.InternalChatMembers
            .FirstOrDefaultAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (membership != null) membership.LastReadAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── DELETE /api/internal-chat/messages/{id} ───────────────────────────────
    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var userId = UserId;
        var msg = await _db.InternalChatMessages.FindAsync(messageId);
        if (msg == null) return NotFound();
        if (msg.SenderId != userId) return Forbid();

        msg.IsDeleted = true;
        msg.Text = "تم حذف هذه الرسالة";
        await _db.SaveChangesAsync();

        await _hub.Clients.All.SendAsync("InternalChatMessageDeleted", new { channelId = msg.ChannelId, messageId });
        return Ok();
    }

    // ── GET /api/internal-chat/users ─────────────────────────────────────────
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var staffIds = await GetStaffUserIdsAsync();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && staffIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                id = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? "موظف") : u.FullName,
                avatarUrl = u.ProfileImageUrl
            })
            .ToListAsync();
        return Ok(users);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private async Task<List<string>> GetStaffUserIdsAsync()
    {
        var staffRoleIds = await _db.Roles
            .Where(r => AppRoles.StaffRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        return await _db.UserRoles
            .Where(ur => staffRoleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();
    }

    private async Task EnsureUserInGeneralChannelAsync(string userId)
    {
        var staffIds = await GetStaffUserIdsAsync();
        if (!staffIds.Contains(userId)) return;

        var general = await _db.InternalChatChannels
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Type == InternalChatChannelType.General);

        if (general == null)
        {
            general = new InternalChatChannel
            {
                Name = "📢 القناة العامة",
                Description = "القناة العامة لجميع موظفي الشركة",
                Type = InternalChatChannelType.General,
                Icon = "📢",
                CreatedByUserId = userId
            };
            _db.InternalChatChannels.Add(general);
            await _db.SaveChangesAsync();
        }

        var isMember = general.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _db.InternalChatMembers.Add(new InternalChatMember
            {
                ChannelId = general.Id,
                UserId = userId,
                UserName = UserName
            });
            await _db.SaveChangesAsync();
        }
    }

    private static object MapMessage(InternalChatMessage m, string currentUserId)
    {
        return new
        {
            id = m.Id,
            channelId = m.ChannelId,
            senderId = m.SenderId,
            senderName = m.SenderName,
            senderAvatarUrl = m.SenderAvatarUrl,
            text = m.IsDeleted ? "🗑️ تم حذف هذه الرسالة" : m.Text,
            isDeleted = m.IsDeleted,
            isEdited = m.IsEdited,
            isMe = m.SenderId == currentUserId,
            replyTo = m.ReplyToMessage == null ? null : new
            {
                id = m.ReplyToMessage.Id,
                senderName = m.ReplyToMessage.SenderName,
                text = m.ReplyToMessage.IsDeleted ? "🗑️ تم حذف هذه الرسالة" : (m.ReplyToMessage.Text.Length > 60 ? m.ReplyToMessage.Text[..60] + "..." : m.ReplyToMessage.Text)
            },
            mentionedUserIds = m.MentionedUserIds,
            linkedEntityType = m.LinkedEntityType,
            linkedEntityId = m.LinkedEntityId,
            linkedEntityRef = m.LinkedEntityRef,
            mediaUrl = m.MediaUrl,
            mediaType = m.MediaType,
            fileName = m.FileName,
            sentAt = m.SentAt,
            readBy = m.ReadReceipts.Select(r => new { r.UserId, r.UserName, r.ReadAt }).ToList()
        };
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────
    public class SendMessageRequest
    {
        public string? Text { get; set; }
        public int? ReplyToMessageId { get; set; }
        public string? MentionedUserIds { get; set; }
        public string? LinkedEntityType { get; set; }
        public int? LinkedEntityId { get; set; }
        public string? LinkedEntityRef { get; set; }
        public string? MediaUrl { get; set; }
        public string? MediaType { get; set; }
        public string? FileName { get; set; }
    }

    public class CreateChannelRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "Department";
        public string? Icon { get; set; }
        public string? TargetUserId { get; set; } // For Direct messages
        public List<string>? MemberIds { get; set; }
    }
}
