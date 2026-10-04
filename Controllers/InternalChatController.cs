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
using Sportive.API.Services;
using Microsoft.Extensions.Logging;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/internal-chat")]
[Authorize]
public class InternalChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly INotificationService _notifications;
    private readonly IInternalChatBotService _chatBot;
    private readonly ILogger<InternalChatController> _logger;

    public InternalChatController(
        AppDbContext db,
        IHubContext<NotificationHub> hub,
        INotificationService notifications,
        IInternalChatBotService chatBot,
        ILogger<InternalChatController> logger)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
        _chatBot = chatBot;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string UserName => User.FindFirstValue("FullName") ?? User.Identity?.Name ?? "موظف";

    // ── GET /api/internal-chat/channels ─────────────────────────────────────────
    /// <summary>Get all channels the current user is a member of, with unread count</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetMyChannels()
    {
        var userId = UserId;
        var userName = UserName;

        // Auto-enroll Admin/Manager to the Partners channel if not already enrolled
        try
        {
            if (User.IsInRole(AppRoles.SuperAdmin) || User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Manager))
            {
                var partnerChan = await _db.InternalChatChannels
                    .FirstOrDefaultAsync(c => c.DirectKey == InternalChatBotService.ChannelKeyPartners);

                if (partnerChan == null)
                {
                    await _chatBot.EnsureSystemChannelsExistAsync();
                    partnerChan = await _db.InternalChatChannels
                        .FirstOrDefaultAsync(c => c.DirectKey == InternalChatBotService.ChannelKeyPartners);
                }

                if (partnerChan != null)
                {
                    var isMember = await _db.InternalChatMembers
                        .AnyAsync(m => m.ChannelId == partnerChan.Id && m.UserId == userId);
                    if (!isMember)
                    {
                        _db.InternalChatMembers.Add(new InternalChatMember
                        {
                            ChannelId = partnerChan.Id,
                            UserId = userId,
                            UserName = userName,
                            JoinedAt = DateTime.UtcNow
                        });
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-enroll partner in GetMyChannels");
        }

        var myMemberships = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId)
            .ToListAsync();

        var channels = await _db.InternalChatChannels
            .AsNoTracking()
            .Include(c => c.Members)
            .Include(c => c.Messages.OrderByDescending(msg => msg.SentAt).Take(1))
            .Where(c => myMemberships.Contains(c.Id) && !c.IsArchived && (c.Type == InternalChatChannelType.Direct || c.Type == InternalChatChannelType.Group))
            .OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt)
            .ToListAsync();

        // Get unread counts accurately per channel
        var memberLastRead = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && myMemberships.Contains(m.ChannelId))
            .ToDictionaryAsync(m => m.ChannelId, m => m.LastReadAt);

        // Fetch unread count grouped by channel for all messages sent by others
        var unreadCounts = await _db.InternalChatMessages
            .AsNoTracking()
            .Where(m => myMemberships.Contains(m.ChannelId) && m.SenderId != userId && !m.IsDeleted)
            .GroupBy(m => m.ChannelId)
            .Select(g => new
            {
                ChannelId = g.Key,
                Messages = g.Select(x => new { x.SentAt })
            })
            .ToListAsync();

        var unreadMap = new Dictionary<int, int>();
        foreach (var ug in unreadCounts)
        {
            var lastRead = memberLastRead.TryGetValue(ug.ChannelId, out var lr) ? lr : null;
            var unread = ug.Messages.Count(m => lastRead == null || m.SentAt > lastRead);
            unreadMap[ug.ChannelId] = unread;
        }

        var result = channels.Select(c =>
        {
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var unread = unreadMap.TryGetValue(c.Id, out var u) ? u : 0;

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
                directKey = c.DirectKey,
                code = c.DirectKey,
                type = c.Type.ToString(),
                icon = c.Icon ?? (c.Type == InternalChatChannelType.Group ? "👥" : null),
                unreadCount = unread,
                membersCount = c.Members.Count,
                createdByUserId = c.CreatedByUserId,
                members = c.Members.Select(m => new { userId = m.UserId, userName = m.UserName, isAdmin = m.IsAdmin }).ToList(),
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
            .Include(m => m.Reactions)
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

        var otherMembers = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.UserId != userId)
            .Select(m => m.UserId)
            .ToListAsync();

        bool areOtherMembersOnline = otherMembers.Any(id => UserPresenceTracker.IsUserOnline(id));

        var result = messages.Select(m => MapMessage(m, userId, null, null, areOtherMembersOnline || m.ReadReceipts.Count > 0)).ToList();

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

        var channel = await _db.InternalChatChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel == null) return NotFound();

        var text = req.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(req.MediaUrl))
        {
            return BadRequest(new { message = "Message content is required" });
        }

        int? replyToId = (req.ReplyToMessageId.HasValue && req.ReplyToMessageId.Value > 0 && req.ReplyToMessageId.Value <= int.MaxValue) 
            ? (int)req.ReplyToMessageId.Value 
            : null;
        if (replyToId.HasValue)
        {
            var replyExists = await _db.InternalChatMessages.AnyAsync(m => m.Id == replyToId.Value && m.ChannelId == channelId);
            if (!replyExists) replyToId = null;
        }

        var msg = new InternalChatMessage
        {
            ChannelId = channelId,
            SenderId = userId,
            SenderName = userName,
            Text = text,
            ReplyToMessageId = replyToId,
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

        // Reload with reply info only if replyTo exists
        if (msg.ReplyToMessageId.HasValue)
        {
            try
            {
                await _db.Entry(msg).Reference(m => m.ReplyToMessage).LoadAsync();
            }
            catch
            {
                // Silently ignore load failure so msg persistence is never jeopardized
            }
        }

        var memberUserIds = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .Select(m => m.UserId)
            .ToListAsync();

        bool isInitiallyDelivered = false;
        if (channel.Type == InternalChatChannelType.Direct)
        {
            var otherId = memberUserIds.FirstOrDefault(id => id != userId);
            if (!string.IsNullOrEmpty(otherId) && UserPresenceTracker.IsUserOnline(otherId))
            {
                isInitiallyDelivered = true;
            }
        }
        else
        {
            isInitiallyDelivered = memberUserIds.Any(id => id != userId && UserPresenceTracker.IsUserOnline(id));
        }

        var mapped = MapMessage(msg, userId, channel.Name, channel.Type.ToString(), isInitiallyDelivered);

        // 🔒 STRICT PRIVACY ISOLATION:
        // Broadcast via SignalR ONLY to verified channel members (Zero leaks to non-members)
        foreach (var memberId in memberUserIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{memberId}").SendAsync("ReceiveInternalChatMessage", mapped);
            }
            catch
            {
                // Silently ignore individual SignalR delivery failure so HTTP 200 is still returned
            }
        }

        // Send mention notifications strictly to mentioned users who are verified members of this channel
        if (!string.IsNullOrWhiteSpace(req.MentionedUserIds))
        {
            var mentionedIds = req.MentionedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var mentionedId in mentionedIds.Where(id => id != userId && memberUserIds.Contains(id)))
            {
                try
                {
                    await _hub.Clients.Group($"user_{mentionedId}").SendAsync("ReceiveInternalChatMention", new
                    {
                        channelId,
                        channelName = channel.Name,
                        messageId = msg.Id,
                        fromName = userName,
                        preview = msg.Text.Length > 80 ? msg.Text[..80] + "..." : msg.Text
                    });
                }
                catch {}
            }
        }

        // 📱 MOBILE & DESKTOP NATIVE WEB PUSH:
        // Send Web Push notifications to devices of all other channel members (wakes up mobile lock screen / background tab)
        var recipientUserIds = memberUserIds.Where(id => id != userId).ToList();
        if (recipientUserIds.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notifications.SendChatWebPushAsync(
                        recipientUserIds,
                        userName,
                        channel.Name,
                        channel.Type == InternalChatChannelType.Group,
                        msg.Text,
                        channelId,
                        msg.MediaType
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send chat web push for channel {ChannelId}", channelId);
                }
            });
        }

        return Ok(mapped);
    }

    // ── GET /api/internal-chat/available ─────────────────────────────────────────
    /// <summary>Get available public channels + staff users for DM</summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable()
    {
        var userId = UserId;

        // STRICT PRIVACY: Only public channels (General) are publicly discoverable.
        // Private groups (Group) are NEVER listed to non-members!
        var publicChannels = await _db.InternalChatChannels
            .AsNoTracking()
            .Include(c => c.Members)
            .Where(c => !c.IsArchived && c.Type == InternalChatChannelType.General)
            .OrderBy(c => c.Name)
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

            // 🔒 Notify ONLY the two participants
            try
            {
                await _hub.Clients.Group($"user_{userId}").SendAsync("InternalChatChannelCreated", new
                {
                    channelId = channel.Id,
                    name = channel.Name,
                    type = "Direct"
                });
                await _hub.Clients.Group($"user_{req.TargetUserId}").SendAsync("InternalChatChannelCreated", new
                {
                    channelId = channel.Id,
                    name = userName,
                    type = "Direct"
                });
            }
            catch {}

            return Ok(new { id = channel.Id, alreadyExists = false });
        }

        // Regular channel / group
        var newChannel = new InternalChatChannel
        {
            Name = req.Name?.Trim() ?? "مجموعة جديدة",
            Description = req.Description,
            Type = Enum.TryParse<InternalChatChannelType>(req.Type, out var ct) ? ct : InternalChatChannelType.Group,
            Icon = req.Icon ?? "👥",
            CreatedByUserId = userId,
            Members = new List<InternalChatMember>
            {
                new() { UserId = userId, UserName = userName, IsAdmin = true }
            }
        };

        if (req.MemberIds != null && req.MemberIds.Count > 0)
        {
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            if (!isAdmin)
            {
                return StatusCode(403, new { message = "فقط مدير النظام (الآدمن) يمتلك صلاحية إضافة موظفين إلى المجموعات." });
            }

            var members = await _db.Users
                .Where(u => req.MemberIds.Contains(u.Id))
                .ToListAsync();
            foreach (var m in members.Where(m => m.Id != userId))
                newChannel.Members.Add(new InternalChatMember { UserId = m.Id, UserName = m.FullName ?? m.UserName ?? "" });
        }

        _db.InternalChatChannels.Add(newChannel);
        await _db.SaveChangesAsync();

        // 🔒 Notify ONLY members of this group (No leak to the rest of the company)
        foreach (var m in newChannel.Members)
        {
            try
            {
                await _hub.Clients.Group($"user_{m.UserId}").SendAsync("InternalChatChannelCreated", new
                {
                    channelId = newChannel.Id,
                    name = newChannel.Name,
                    type = newChannel.Type.ToString()
                });
            }
            catch {}
        }

        return Ok(new { id = newChannel.Id });
    }

    // ── POST /api/internal-chat/channels/{id}/join ────────────────────────────
    [HttpPost("channels/{channelId}/join")]
    public async Task<IActionResult> JoinChannel(int channelId)
    {
        var userId = UserId;
        var channel = await _db.InternalChatChannels.FindAsync(channelId);
        if (channel == null) return NotFound();

        // 🔒 STRICT PRIVACY: Only public General channels allow self-joining!
        // Private Direct chats and private groups CANNOT be joined arbitrarily without invitation!
        if (channel.Type != InternalChatChannelType.General) 
            return Forbid();

        var already = await _db.InternalChatMembers
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (already) return Ok(new { success = true });

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

        // Verify channel membership
        var isMember = await _db.InternalChatMembers.AnyAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (!isMember) return Forbid();

        var already = await _db.InternalChatReadReceipts
            .AnyAsync(r => r.MessageId == messageId && r.UserId == userId);
        if (!already)
        {
            _db.InternalChatReadReceipts.Add(new InternalChatReadReceipt
            {
                MessageId = messageId,
                UserId = userId,
                UserName = UserName
            });
        }

        // Ensure delivery receipt is also recorded
        var alreadyDelivered = await _db.InternalChatDeliveryReceipts
            .AnyAsync(r => r.MessageId == messageId && r.UserId == userId);
        if (!alreadyDelivered)
        {
            _db.InternalChatDeliveryReceipts.Add(new InternalChatDeliveryReceipt
            {
                MessageId = messageId,
                UserId = userId,
                UserName = UserName,
                DeliveredAt = DateTime.UtcNow
            });
        }

        // Update channel last read
        var membership = await _db.InternalChatMembers
            .FirstOrDefaultAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (membership != null) membership.LastReadAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent receipt already written by another request, ignore
        }

        // Notify the message sender for real-time double blue check
        if (msg.SenderId != userId)
        {
            try
            {
                await _hub.Clients.Group($"user_{msg.SenderId}").SendAsync("InternalChatMessageRead", new
                {
                    channelId = msg.ChannelId,
                    messageId = msg.Id,
                    userId,
                    userName = UserName,
                    readAt = DateTime.UtcNow
                });
            }
            catch {}
        }

        return Ok();
    }

    // ── POST /api/internal-chat/messages/{id}/delivered ──────────────────────
    [HttpPost("messages/{messageId}/delivered")]
    public async Task<IActionResult> MarkDelivered(int messageId)
    {
        var userId = UserId;
        var msg = await _db.InternalChatMessages.FindAsync(messageId);
        if (msg == null) return NotFound();

        var isMember = await _db.InternalChatMembers.AnyAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (!isMember) return Forbid();

        // Persist delivery receipt in database
        var already = await _db.InternalChatDeliveryReceipts
            .AnyAsync(r => r.MessageId == messageId && r.UserId == userId);
        if (!already)
        {
            _db.InternalChatDeliveryReceipts.Add(new InternalChatDeliveryReceipt
            {
                MessageId = messageId,
                UserId = userId,
                UserName = UserName,
                DeliveredAt = DateTime.UtcNow
            });
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Concurrent delivery receipt already written, ignore
            }
        }

        // Notify message sender that message was delivered to recipient (Real-time double grey check)
        if (msg.SenderId != userId)
        {
            try
            {
                await _hub.Clients.Group($"user_{msg.SenderId}").SendAsync("InternalChatMessageDelivered", new
                {
                    channelId = msg.ChannelId,
                    messageId = msg.Id,
                    deliveredToUserId = userId
                });
            }
            catch {}
        }

        return Ok(new { success = true });
    }

    // ── GET /api/internal-chat/messages/{id}/info ─────────────────────────────
    /// <summary>Detailed delivery & read breakdown per member for group messages</summary>
    [HttpGet("messages/{messageId}/info")]
    public async Task<IActionResult> GetMessageInfo(int messageId)
    {
        var userId = UserId;
        var msg = await _db.InternalChatMessages
            .AsNoTracking()
            .Include(m => m.ReadReceipts)
            .Include(m => m.DeliveryReceipts)
            .Include(m => m.Channel)
                .ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (msg == null) return NotFound(new { message = "الرسالة غير موجودة" });

        // Caller must be a member of the channel
        var isMember = msg.Channel.Members.Any(m => m.UserId == userId);
        if (!isMember) return Forbid();

        // Get profile image and info for all members
        var memberUserIds = msg.Channel.Members.Select(m => m.UserId).Distinct().ToList();
        var userProfiles = (await _db.Users
            .AsNoTracking()
            .Where(u => memberUserIds.Contains(u.Id))
            .ToListAsync())
            .GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => new
            {
                Name = string.IsNullOrWhiteSpace(g.First().FullName) ? (g.First().UserName ?? "موظف") : g.First().FullName,
                AvatarUrl = g.First().ProfileImageUrl
            });

        var readMap = msg.ReadReceipts
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.ReadAt));

        var deliveryMap = msg.DeliveryReceipts
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.DeliveredAt));

        // Recipients are all channel members except the message sender
        var recipients = msg.Channel.Members
            .Where(m => m.UserId != msg.SenderId)
            .GroupBy(m => m.UserId)
            .Select(g => g.First())
            .ToList();

        var readList = new List<object>();
        var deliveredList = new List<object>();
        var pendingList = new List<object>();

        foreach (var rec in recipients)
        {
            var prof = userProfiles.TryGetValue(rec.UserId, out var p) ? p : null;
            var displayName = prof?.Name ?? rec.UserName;
            var avatarUrl = prof?.AvatarUrl;
            bool isOnline = UserPresenceTracker.IsUserOnline(rec.UserId);

            // Read detection: direct receipt OR member opened channel after message was sent
            bool isRead = readMap.TryGetValue(rec.UserId, out var readAt);
            if (!isRead && rec.LastReadAt.HasValue && rec.LastReadAt.Value >= msg.SentAt)
            {
                isRead = true;
                readAt = rec.LastReadAt.Value;
            }

            // Delivery detection: delivered receipt OR isRead OR member currently online OR member was active
            DateTime? deliveredAt = null;
            bool hasDeliveryReceipt = deliveryMap.TryGetValue(rec.UserId, out var dReceiptTime);
            if (hasDeliveryReceipt)
            {
                deliveredAt = dReceiptTime;
            }

            bool isDelivered = isRead || hasDeliveryReceipt;
            if (!isDelivered && (isOnline || (rec.LastReadAt.HasValue && rec.LastReadAt.Value >= msg.SentAt)))
            {
                isDelivered = true;
                deliveredAt = msg.SentAt;
            }

            if (isRead)
            {
                readList.Add(new
                {
                    userId = rec.UserId,
                    userName = displayName,
                    avatarUrl,
                    isAdmin = rec.IsAdmin,
                    isOnline,
                    readAt,
                    deliveredAt = deliveryMap.TryGetValue(rec.UserId, out var dAt) ? (DateTime?)dAt : readAt
                });
            }
            else if (isDelivered)
            {
                deliveredList.Add(new
                {
                    userId = rec.UserId,
                    userName = displayName,
                    avatarUrl,
                    isAdmin = rec.IsAdmin,
                    isOnline,
                    deliveredAt = (DateTime?)deliveredAt
                });
            }
            else
            {
                pendingList.Add(new
                {
                    userId = rec.UserId,
                    userName = displayName,
                    avatarUrl,
                    isAdmin = rec.IsAdmin,
                    isOnline
                });
            }
        }

        return Ok(new
        {
            message = new
            {
                id = msg.Id,
                channelId = msg.ChannelId,
                channelName = msg.Channel?.Name,
                senderId = msg.SenderId,
                senderName = msg.SenderName,
                text = msg.IsDeleted ? "🗑️ تم حذف هذه الرسالة" : msg.Text,
                mediaUrl = msg.MediaUrl,
                mediaType = msg.MediaType,
                fileName = msg.FileName,
                sentAt = msg.SentAt
            },
            summary = new
            {
                totalRecipients = recipients.Count,
                readCount = readList.Count,
                deliveredCount = deliveredList.Count + readList.Count,
                pendingCount = pendingList.Count
            },
            read = readList,
            delivered = deliveredList,
            pending = pendingList
        });
    }

    // ── DELETE /api/internal-chat/messages/{id} ───────────────────────────────
    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var userId = UserId;
        var msg = await _db.InternalChatMessages.FindAsync(messageId);
        if (msg == null) return NotFound();

        var isMember = await _db.InternalChatMembers.AnyAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (!isMember) return Forbid();

        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (msg.SenderId != userId && !isAdmin) return Forbid();

        msg.IsDeleted = true;
        msg.Text = "تم حذف هذه الرسالة";
        await _db.SaveChangesAsync();

        // 🔒 Notify ONLY channel members
        var memberIds = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.ChannelId == msg.ChannelId)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var mId in memberIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{mId}").SendAsync("InternalChatMessageDeleted", new { channelId = msg.ChannelId, messageId });
            }
            catch {}
        }

        return Ok();
    }

    // ── DELETE /api/internal-chat/channels/{id} ───────────────────────────────
    [HttpDelete("channels/{channelId}")]
    public async Task<IActionResult> DeleteChannel(int channelId)
    {
        var userId = UserId;
        var channel = await _db.InternalChatChannels
            .Include(c => c.Members)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel == null) return NotFound();

        // 🔒 Security check: Only creator or admin can delete channel
        var canDelete = channel.CreatedByUserId == userId 
            || channel.Members.Any(m => m.UserId == userId && m.IsAdmin) 
            || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!canDelete) return Forbid();

        var memberUserIds = channel.Members.Select(m => m.UserId).ToList();

        _db.InternalChatChannels.Remove(channel);
        await _db.SaveChangesAsync();

        // 🔒 Notify only members of this channel
        foreach (var mId in memberUserIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{mId}").SendAsync("InternalChatChannelDeleted", new { channelId });
            }
            catch {}
        }

        return Ok(new { success = true });
    }

    // ── POST /api/internal-chat/channels/{id}/members ─────────────────────────
    [HttpPost("channels/{channelId}/members")]
    public async Task<IActionResult> AddMember(int channelId, [FromBody] AddMemberRequest req)
    {
        var userId = UserId;
        var channel = await _db.InternalChatChannels.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel == null || channel.Type == InternalChatChannelType.Direct) return BadRequest("Invalid channel");

        // 🔒 STRICT SECURITY: ONLY System Admin or SuperAdmin can add members to groups!
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!isAdmin)
        {
            return StatusCode(403, new { message = "فقط مدير النظام (الآدمن) يمتلك صلاحية إضافة موظفين إلى المجموعات." });
        }

        if (string.IsNullOrEmpty(req.UserId)) return BadRequest("User ID is required");

        var already = channel.Members.Any(m => m.UserId == req.UserId);
        if (already) return Ok(new { success = true, alreadyMember = true });

        var user = await _db.Users.FindAsync(req.UserId);
        if (user == null) return NotFound("User not found");

        var newMember = new InternalChatMember
        {
            ChannelId = channelId,
            UserId = req.UserId,
            UserName = user.FullName ?? user.UserName ?? "عضو"
        };
        _db.InternalChatMembers.Add(newMember);
        await _db.SaveChangesAsync();

        // 🔒 Notify the newly added member
        try
        {
            await _hub.Clients.Group($"user_{req.UserId}").SendAsync("InternalChatChannelCreated", new
            {
                channelId = channel.Id,
                name = channel.Name,
                type = channel.Type.ToString()
            });
        }
        catch {}

        // 🔒 Notify existing members
        foreach (var m in channel.Members)
        {
            try
            {
                await _hub.Clients.Group($"user_{m.UserId}").SendAsync("InternalChatChannelUpdated", new { channelId });
            }
            catch {}
        }

        return Ok(new { success = true });
    }

    // ── DELETE /api/internal-chat/channels/{id}/members/{targetUserId} ────────
    [HttpDelete("channels/{channelId}/members/{targetUserId}")]
    public async Task<IActionResult> RemoveMember(int channelId, string targetUserId)
    {
        var userId = UserId;
        var channel = await _db.InternalChatChannels.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel == null) return NotFound();

        // 🔒 Security check: user can leave themselves, OR creator/channel admin/system admin can kick
        var canRemove = targetUserId == userId 
            || channel.CreatedByUserId == userId 
            || channel.Members.Any(m => m.UserId == userId && m.IsAdmin) 
            || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!canRemove) return Forbid();

        var membership = channel.Members.FirstOrDefault(m => m.UserId == targetUserId);
        if (membership == null) return NotFound();

        _db.InternalChatMembers.Remove(membership);
        await _db.SaveChangesAsync();

        // 🔒 Notify the removed user
        try
        {
            await _hub.Clients.Group($"user_{targetUserId}").SendAsync("InternalChatChannelDeleted", new { channelId });
        }
        catch {}

        // 🔒 Notify remaining members
        var remainingIds = channel.Members.Where(m => m.UserId != targetUserId).Select(m => m.UserId).ToList();
        foreach (var rId in remainingIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{rId}").SendAsync("InternalChatChannelUpdated", new { channelId });
            }
            catch {}
        }

        return Ok(new { success = true });
    }

    // ── POST /api/internal-chat/channels/cleanup-departments ──────────────────
    [HttpPost("channels/cleanup-departments")]
    public async Task<IActionResult> CleanupDepartmentChannels()
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin")) return Forbid();

        var depts = await _db.InternalChatChannels
            .Where(c => c.Type != InternalChatChannelType.Direct && c.Type != InternalChatChannelType.Group)
            .ToListAsync();
        _db.InternalChatChannels.RemoveRange(depts);
        await _db.SaveChangesAsync();
        return Ok(new { deletedCount = depts.Count });
    }

    // ── GET /api/internal-chat/online-users ──────────────────────────────────────
    [HttpGet("online-users")]
    public IActionResult GetOnlineUsers()
    {
        return Ok(UserPresenceTracker.GetOnlineUsers());
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

    private static object MapMessage(InternalChatMessage m, string currentUserId, string? channelName = null, string? channelType = null, bool isDelivered = false)
    {
        return new
        {
            id = m.Id,
            channelId = m.ChannelId,
            channelName = channelName ?? m.Channel?.Name,
            channelType = channelType ?? m.Channel?.Type.ToString(),
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
            isDelivered = isDelivered || m.ReadReceipts.Count > 0,
            readBy = m.ReadReceipts.Select(r => new { r.UserId, r.UserName, r.ReadAt }).ToList(),
            reactions = m.Reactions == null ? Array.Empty<object>() : m.Reactions.Select(r => (object)new { r.UserId, r.UserName, r.Emoji }).ToArray()
        };
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────
    public class SendMessageRequest
    {
        public string? Text { get; set; }
        public long? ReplyToMessageId { get; set; }
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
        public string Type { get; set; } = "Group";
        public string? Icon { get; set; }
        public string? TargetUserId { get; set; } // For Direct messages
        public List<string>? MemberIds { get; set; }
    }

    public class AddMemberRequest
    {
        public string UserId { get; set; } = "";
    }

    public class ReactRequest
    {
        public string Emoji { get; set; } = "";
    }

    // ── React to Message ───────────────────────────────────────────────────────
    [HttpPost("messages/{messageId}/react")]
    public async Task<IActionResult> ReactToMessage(int messageId, [FromBody] ReactRequest req)
    {
        var userId = UserId;
        var userName = User.FindFirstValue(ClaimTypes.Name)
                    ?? User.FindFirstValue("name")
                    ?? User.FindFirstValue("FullName")
                    ?? userId;

        var msg = await _db.InternalChatMessages.FindAsync(messageId);
        if (msg == null) return NotFound();

        var isMember = await _db.InternalChatMembers
            .AnyAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId);
        if (!isMember) return Forbid();

        var existing = await _db.InternalChatReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId);

        if (existing != null && existing.Emoji == req.Emoji)
        {
            _db.InternalChatReactions.Remove(existing); // toggle off
        }
        else
        {
            if (existing != null) _db.InternalChatReactions.Remove(existing);
            _db.InternalChatReactions.Add(new InternalChatReaction
            {
                MessageId = messageId,
                UserId = userId,
                UserName = userName,
                Emoji = req.Emoji,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        var reactions = await _db.InternalChatReactions
            .Where(r => r.MessageId == messageId)
            .Select(r => new { r.UserId, r.UserName, r.Emoji })
            .ToListAsync();

        await _hub.Clients.Group($"channel_{msg.ChannelId}")
            .SendAsync("MessageReacted", new { messageId, reactions });

        return Ok(new { messageId, reactions });
    }

    // ── Bot Operations ────────────────────────────────────────────────────────
    public class BotCommandRequest
    {
        public int ChannelId { get; set; }
        public string Command { get; set; } = "";
        public string Query { get; set; } = "";
    }

    public class BotClaimRequest
    {
        public int MessageId { get; set; }
        public string Action { get; set; } = "claim"; // "claim" or "resolve"
        public string? Note { get; set; }
    }

    [HttpPost("bot/command")]
    public async Task<IActionResult> ExecuteBotCommand([FromBody] BotCommandRequest req)
    {
        try
        {
            var res = await _chatBot.ExecuteSlashCommandAsync(req.ChannelId, UserId, UserName, req.Command, req.Query);
            return Ok(res);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("bot/claim")]
    public async Task<IActionResult> ClaimBotMessage([FromBody] BotClaimRequest req)
    {
        var res = await _chatBot.ClaimOrResolveMessageAsync(req.MessageId, UserId, UserName, req.Action, req.Note);
        return Ok(res);
    }
}
