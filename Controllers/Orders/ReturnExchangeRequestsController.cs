using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.DTOs;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class ReturnExchangeRequestsController : ControllerBase
{
	private readonly AppDbContext _db;

	private readonly IHubContext<NotificationHub> _hubContext;

	private readonly ILogger<ReturnExchangeRequestsController> _logger;

	private readonly IAccountingService _accounting;

	private readonly INotificationService _notificationService;

	private readonly IInventoryService _inventory;

	public ReturnExchangeRequestsController(AppDbContext db, IHubContext<NotificationHub> hubContext, ILogger<ReturnExchangeRequestsController> logger, IAccountingService accounting, INotificationService notificationService, IInventoryService inventory)
	{
		_db = db;
		_hubContext = hubContext;
		_logger = logger;
		_accounting = accounting;
		_notificationService = notificationService;
		_inventory = inventory;
	}

	[HttpPost("{orderId}/return-exchange-request")]
	public async Task<IActionResult> SubmitRequest(string orderId, [FromBody] CreateReturnExchangeRequestDto dto)
	{
		int.TryParse(orderId, out var idInt);
		Order order = await _db.Orders.Include((Order o) => o.Items).FirstOrDefaultAsync((Order o) => o.Id == idInt || o.OrderNumber == orderId);
		if (order == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		Customer customer = await GetCurrentCustomerAsync();
		if (customer == null)
		{
			customer = await _db.Customers.FirstOrDefaultAsync((Customer c) => c.Id == order.CustomerId);
		}
		int customerId = customer?.Id ?? order.CustomerId;
		return await ProcessItemDeletionOrRequestAsync(order, customerId, dto);
	}

	[HttpPost("public-return-exchange-request")]
	[AllowAnonymous]
	public async Task<IActionResult> PublicSubmitRequest([FromQuery] string orderNumber, [FromBody] CreateReturnExchangeRequestDto dto)
	{
		if (string.IsNullOrWhiteSpace(orderNumber))
		{
			return BadRequest("رقم الطلب مطلوب.");
		}
		Order order = await _db.Orders.Include((Order o) => o.Items).FirstOrDefaultAsync((Order o) => o.OrderNumber == orderNumber || o.Id.ToString() == orderNumber);
		if (order == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		return await ProcessItemDeletionOrRequestAsync(order, order.CustomerId, dto);
	}

	[HttpGet("public-status/{orderNumber}")]
	[AllowAnonymous]
	public async Task<IActionResult> GetPublicOrderStatus(string orderNumber)
	{
		Order order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync((Order o) => o.OrderNumber == orderNumber || o.Id.ToString() == orderNumber);
		if (order == null)
		{
			return NotFound();
		}
		List<ReturnExchangeRequestResponseDto> value = (await (from r in _db.ReturnExchangeRequests.AsNoTracking().Include((ReturnExchangeRequest r) => r.Items).ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			where r.OrderId == order.Id
			orderby r.CreatedAt descending
			select r).ToListAsync()).Select(MapToResponseDto).ToList();
		return Ok(value);
	}

	private async Task<IActionResult> ProcessItemDeletionOrRequestAsync(Order order, int customerId, CreateReturnExchangeRequestDto dto)
	{
		bool flag = string.Equals(dto.Type, "Delete", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(dto.Type, "Add", StringComparison.OrdinalIgnoreCase) || string.Equals(dto.Type, "AddProduct", StringComparison.OrdinalIgnoreCase);
		bool flag3 = string.Equals(dto.Type, "Exchange", StringComparison.OrdinalIgnoreCase);
		if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Returned)
		{
			return BadRequest("لا يمكن تقديم طلبات استبدال أو استرجاع أو إضافة على طلب ملغى أو مرجع بالكامل.");
		}
		if (flag | flag2)
		{
			OrderStatus[] source = new OrderStatus[4]
			{
				OrderStatus.OutForDelivery,
				OrderStatus.Delivered,
				OrderStatus.Cancelled,
				OrderStatus.Returned
			};
			if (Enumerable.Contains(source, order.Status))
			{
				return BadRequest("لا يمكن حذف أو إضافة أصناف مباشرة للفاتورة بعد شحن الطلب أو تسليمه.");
			}
		}
		else if ((flag3 || string.Equals(dto.Type, "Return", StringComparison.OrdinalIgnoreCase)) && order.Status == OrderStatus.Delivered && !base.User.IsInRole("SuperAdmin") && !base.User.IsInRole("Admin") && !base.User.IsInRole("Manager"))
		{
			DateTime dateTime = ((order.UpdatedAt.HasValue && order.UpdatedAt.Value > order.CreatedAt) ? order.UpdatedAt.Value : order.CreatedAt);
			double totalDays = (TimeHelper.GetEgyptTime() - dateTime).TotalDays;
			if (totalDays > 14.0)
			{
				return BadRequest("تجاوزت الفترة المسموحة لطلب الاستبدال أو الاسترجاع (14 يوما\u064b من تاريخ الاستلام).");
			}
		}
		if (dto.Items == null || !dto.Items.Any())
		{
			return BadRequest("يرجى اختيار صنف واحد على الأقل.");
		}
		if (flag2)
		{
			List<string> addedItemsNotes = new List<string>();
			foreach (ReturnExchangeItemInputDto itemDto in dto.Items)
			{
				Product product = null;
				ProductVariant variant = null;
				if (itemDto.ProductVariantId.HasValue && itemDto.ProductVariantId.Value > 0)
				{
					variant = await _db.ProductVariants.Include((ProductVariant v) => v.Product).FirstOrDefaultAsync((ProductVariant v) => v.Id == itemDto.ProductVariantId.Value);
					if (variant != null)
					{
						product = variant.Product;
					}
				}
				if (product == null && itemDto.ProductId.HasValue && itemDto.ProductId.Value > 0)
				{
					product = await _db.Products.Include((Product p) => p.Variants).FirstOrDefaultAsync((Product p) => p.Id == itemDto.ProductId.Value);
				}
				if (product == null && itemDto.OrderItemId > 0)
				{
					product = await _db.Products.Include((Product p) => p.Variants).FirstOrDefaultAsync((Product p) => p.Id == itemDto.OrderItemId);
				}
				if (product == null && !string.IsNullOrWhiteSpace(itemDto.ReplacementNote))
				{
					string replacementNote = itemDto.ReplacementNote;
					string cleanTitle = replacementNote.Replace("بديل:", "").Replace("إضافة:", "").Split('(')[0].Trim();
					if (!string.IsNullOrWhiteSpace(cleanTitle))
					{
						product = await _db.Products.Include((Product p) => p.Variants).FirstOrDefaultAsync((Product p) => p.NameAr.Contains(cleanTitle) || p.NameEn.Contains(cleanTitle));
					}
				}
				if (product == null)
				{
					continue;
				}
				int qtyToAdd = Math.Max(1, itemDto.Quantity);
				string size = itemDto.Size ?? "";
				string color = itemDto.Color ?? "";
				if ((string.IsNullOrEmpty(size) || string.IsNullOrEmpty(color)) && !string.IsNullOrWhiteSpace(itemDto.ReplacementNote))
				{
					string replacementNote2 = itemDto.ReplacementNote;
					if (string.IsNullOrEmpty(color) && replacementNote2.Contains("لون:"))
					{
						string text = replacementNote2.Substring(replacementNote2.IndexOf("لون:") + 4);
						color = text.Split(new char[2] { '|', ')' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
					}
					if (string.IsNullOrEmpty(size) && replacementNote2.Contains("مقاس:"))
					{
						string text2 = replacementNote2.Substring(replacementNote2.IndexOf("مقاس:") + 5);
						size = text2.Split(new char[2] { '|', ')' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
					}
				}
				if (variant == null && product.Variants != null && product.Variants.Any())
				{
					variant = product.Variants.FirstOrDefault((ProductVariant v) => (string.IsNullOrEmpty(size) || v.Size == size) && (string.IsNullOrEmpty(color) || v.ColorAr == color || v.Color == color));
					if (variant == null)
					{
						variant = product.Variants.FirstOrDefault();
					}
				}
				if (variant != null)
				{
					variant.StockQuantity = Math.Max(0, variant.StockQuantity - qtyToAdd);
					variant.UpdatedAt = TimeHelper.GetEgyptTime();
				}
				else
				{
					product.TotalStock = Math.Max(0, product.TotalStock - qtyToAdd);
					product.UpdatedAt = TimeHelper.GetEgyptTime();
				}
				ProductWarehouseStock productWarehouseStock = await _db.ProductWarehouseStocks.Include((ProductWarehouseStock ws) => ws.ProductVariant).FirstOrDefaultAsync((ProductWarehouseStock ws) => (variant != null && ws.ProductVariantId == variant.Id) || (variant == null && ws.ProductVariant != null && ws.ProductVariant.ProductId == product.Id));
				if (productWarehouseStock != null)
				{
					productWarehouseStock.Quantity = Math.Max(0, productWarehouseStock.Quantity - qtyToAdd);
					productWarehouseStock.UpdatedAt = TimeHelper.GetEgyptTime();
				}
				decimal num = ((product.DiscountPrice.HasValue && product.DiscountPrice.Value > 0m) ? product.DiscountPrice.Value : product.Price);
				decimal price = product.Price;
				decimal discountAmount = ((price > num) ? (price - num) : 0m);
				OrderItem orderItem = order.Items.FirstOrDefault((OrderItem i) => i.ProductId == product.Id && ((variant != null && i.ProductVariantId == variant.Id) || (variant == null && i.Size == size && i.Color == color)));
				if (orderItem != null)
				{
					orderItem.Quantity += qtyToAdd;
					orderItem.TotalPrice = (decimal)orderItem.Quantity * num;
				}
				else
				{
					OrderItem item = new OrderItem
					{
						OrderId = order.Id,
						ProductId = product.Id,
						ProductVariantId = variant?.Id,
						ProductNameAr = product.NameAr,
						ProductNameEn = product.NameEn,
						SKU = ((!string.IsNullOrWhiteSpace(product.SKU)) ? product.SKU : ""),
						Size = ((!string.IsNullOrWhiteSpace(size)) ? size : (variant?.Size ?? "")),
						Color = ((!string.IsNullOrWhiteSpace(color)) ? color : (variant?.ColorAr ?? variant?.Color ?? "")),
						Quantity = qtyToAdd,
						UnitPrice = num,
						OriginalUnitPrice = price,
						DiscountAmount = discountAmount,
						TotalPrice = (decimal)qtyToAdd * num,
						CreatedAt = TimeHelper.GetEgyptTime()
					};
					order.Items.Add(item);
				}
				string value = ((!string.IsNullOrWhiteSpace(product.NameAr)) ? product.NameAr : product.NameEn);
				addedItemsNotes.Add($"{value} - {color} {size} (كمية: {qtyToAdd})");
			}
			if (!addedItemsNotes.Any())
			{
				return BadRequest("تعذر تحديد المنتج المراد إضافته.");
			}
			decimal num2 = order.Items.Sum((OrderItem i) => i.UnitPrice * (decimal)i.Quantity);
			decimal num3 = order.Items.Sum((OrderItem i) => i.ItemVatAmount);
			order.SubTotal = num2;
			order.TotalVatAmount = num3;
			order.TemporalDiscount = order.Items.Sum((OrderItem i) => i.DiscountAmount * (decimal)i.Quantity);
			order.TotalAmount = Math.Max(0m, num2 + order.DeliveryFee - order.DiscountAmount + num3);
			order.UpdatedAt = TimeHelper.GetEgyptTime();
			order.AdminNotes += $" | [إضافة منتجات بواسطة العميل: {string.Join(", ", addedItemsNotes)} بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
			await _db.SaveChangesAsync();
			try
			{
				await _accounting.PostSalesOrderAsync(order);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to update accounting journal entry for order product addition {OrderNo}", order.OrderNumber);
			}
			try
			{
				if (_notificationService != null)
				{
					await _notificationService.SendAsync(null, "إضافة صنف جديد للفاتورة ➕", "Item Added to Order Invoice", $"قام العميل بإضافة أصناف جديدة للفاتورة رقم #{order.OrderNumber}: {string.Join(", ", addedItemsNotes)} (الإجمالي الجديد: {order.TotalAmount:N2} ج.م)", "Customer added items to order #" + order.OrderNumber + ": " + string.Join(", ", addedItemsNotes), "Order", order.Id);
				}
			}
			catch
			{
			}
			try
			{
				await _hubContext.Clients.All.SendAsync("DashboardUpdate", new
				{
					type = "OrderUpdated",
					id = order.Id
				});
				await _hubContext.Clients.All.SendAsync("DashboardUpdated", new
				{
					type = "OrderUpdated",
					id = order.Id
				});
			}
			catch
			{
			}
			return Ok(new
			{
				message = "تمت إضافة الصنف للفاتورة وتحديث الإجمالي والمخزن والقيد المحاسبي فورا\u064b \ud83c\udf1f",
				isAddedDirectly = true,
				newTotal = order.TotalAmount,
				orderStatus = order.Status.ToString()
			});
		}
		if (flag)
		{
			List<string> addedItemsNotes = new List<string>();
			foreach (ReturnExchangeItemInputDto itemDto2 in dto.Items)
			{
				OrderItem orderItem2 = order.Items.FirstOrDefault((OrderItem i) => i.Id == itemDto2.OrderItemId || i.ProductId == itemDto2.OrderItemId);
				if (orderItem2 == null)
				{
					continue;
				}
				int qtyToAdd = ((itemDto2.Quantity > 0) ? Math.Min(itemDto2.Quantity, orderItem2.Quantity) : orderItem2.Quantity);
				if (orderItem2.ProductVariantId.HasValue)
				{
					ProductVariant productVariant = await _db.ProductVariants.FindAsync(orderItem2.ProductVariantId.Value);
					if (productVariant != null)
					{
						productVariant.StockQuantity += qtyToAdd;
						productVariant.UpdatedAt = TimeHelper.GetEgyptTime();
					}
				}
				else
				{
					Product product2 = await _db.Products.FindAsync(orderItem2.ProductId);
					if (product2 != null)
					{
						product2.TotalStock += qtyToAdd;
						product2.UpdatedAt = TimeHelper.GetEgyptTime();
					}
				}
				orderItem2.Quantity -= qtyToAdd;
				string value2 = ((!string.IsNullOrWhiteSpace(orderItem2.ProductNameAr)) ? orderItem2.ProductNameAr : orderItem2.ProductNameEn);
				addedItemsNotes.Add($"{value2} (كمية: {qtyToAdd})");
				if (orderItem2.Quantity <= 0)
				{
					_db.OrderItems.Remove(orderItem2);
				}
			}
			List<OrderItem> remainingItems = order.Items.Where((OrderItem i) => _db.Entry(i).State != EntityState.Deleted && i.Quantity > 0).ToList();
			decimal num4 = ((order.SubTotal > 0m) ? order.SubTotal : (remainingItems.Sum((OrderItem i) => i.UnitPrice * (decimal)i.Quantity) + (decimal)(addedItemsNotes.Count * 100)));
			decimal num5 = remainingItems.Sum((OrderItem i) => i.UnitPrice * (decimal)i.Quantity);
			decimal num6 = remainingItems.Sum((OrderItem i) => i.ItemVatAmount);
			if (num4 > 0m && order.DiscountAmount > 0m)
			{
				decimal num7 = Math.Min(1m, num5 / num4);
				order.DiscountAmount = Math.Round(order.DiscountAmount * num7, 2);
			}
			else if (num5 < order.DiscountAmount)
			{
				order.DiscountAmount = num5;
			}
			order.SubTotal = num5;
			order.TotalVatAmount = num6;
			order.TotalAmount = Math.Max(0m, num5 + order.DeliveryFee - order.DiscountAmount + num6);
			order.UpdatedAt = TimeHelper.GetEgyptTime();
			if (!remainingItems.Any())
			{
				order.Status = OrderStatus.Cancelled;
				order.AdminNotes += $" | [إلغاء الطلب بحذف جميع الأصناف بواسطة العميل بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
			}
			else
			{
				order.AdminNotes += $" | [حذف أصناف بواسطة العميل: {string.Join(", ", addedItemsNotes)} بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
			}
			await _db.SaveChangesAsync();
			try
			{
				if (remainingItems.Any())
				{
					await _accounting.PostSalesOrderAsync(order);
				}
				else
				{
					JournalEntry journalEntry = await _db.JournalEntries.FirstOrDefaultAsync((JournalEntry e) => ((int)e.Type == 2 || (int)e.Type == 2) && e.Reference == order.OrderNumber);
					if (journalEntry != null && journalEntry.Status != JournalEntryStatus.Reversed)
					{
						await _accounting.ReverseEntryAsync(journalEntry.Id, "إلغاء الفاتورة رقم " + order.OrderNumber + " بحذف جميع الأصناف");
					}
				}
			}
			catch (Exception exception2)
			{
				_logger.LogError(exception2, "Failed to update accounting journal entry for order deletion {OrderNo}", order.OrderNumber);
			}
			try
			{
				if (_notificationService != null)
				{
					await _notificationService.SendAsync(null, "حذف صنف من فاتورة ⚠\ufe0f", "Item Deleted from Order", "قام العميل بحذف أصناف من الفاتورة رقم #" + order.OrderNumber + ": " + string.Join(", ", addedItemsNotes), "Customer deleted items from order #" + order.OrderNumber + ": " + string.Join(", ", addedItemsNotes), "Order", order.Id);
				}
			}
			catch
			{
			}
			try
			{
				await _hubContext.Clients.All.SendAsync("DashboardUpdate", new
				{
					type = "OrderUpdated",
					id = order.Id
				});
				await _hubContext.Clients.All.SendAsync("DashboardUpdated", new
				{
					type = "OrderUpdated",
					id = order.Id
				});
			}
			catch
			{
			}
			return Ok(new
			{
				message = (remainingItems.Any() ? "تم حذف الصنف من الفاتورة وتحديث الإجمالي والمخزن والقيد المحاسبي فورا\u064b \ud83c\udf1f" : "تم حذف جميع الأصناف وإلغاء الفاتورة بنجاح."),
				isDeletedDirectly = true,
				newTotal = order.TotalAmount,
				orderStatus = order.Status.ToString()
			});
		}
		ReturnExchangeType reqType = (flag3 ? ReturnExchangeType.Exchange : ReturnExchangeType.Return);
		ReturnExchangeRequest request = new ReturnExchangeRequest
		{
			OrderId = order.Id,
			CustomerId = customerId,
			Type = reqType,
			Status = ReturnExchangeStatus.Pending,
			Reason = (dto.Reason ?? "طلب تعديل / استبدال صنف"),
			CustomerNotes = dto.CustomerNotes,
			CreatedAt = TimeHelper.GetEgyptTime()
		};
		foreach (ReturnExchangeItemInputDto itemDto3 in dto.Items)
		{
			OrderItem orderItem3 = order.Items.FirstOrDefault((OrderItem i) => i.Id == itemDto3.OrderItemId || (i.ProductId.HasValue && i.ProductId.Value == itemDto3.OrderItemId) || (i.ProductVariantId.HasValue && i.ProductVariantId.Value == itemDto3.OrderItemId));
			if (orderItem3 != null)
			{
				int val = Math.Max(1, orderItem3.Quantity - orderItem3.ReturnedQuantity);
				int quantity = ((itemDto3.Quantity <= 0) ? 1 : Math.Min(itemDto3.Quantity, val));
				request.Items.Add(new ReturnExchangeRequestItem
				{
					OrderItemId = orderItem3.Id,
					Quantity = quantity,
					ReplacementNote = ((!string.IsNullOrWhiteSpace(itemDto3.ReplacementNote)) ? itemDto3.ReplacementNote : dto.CustomerNotes),
					CreatedAt = TimeHelper.GetEgyptTime()
				});
			}
		}
		if (!request.Items.Any() && order.Items.Any())
		{
			OrderItem orderItem4 = order.Items.First();
			request.Items.Add(new ReturnExchangeRequestItem
			{
				OrderItemId = orderItem4.Id,
				Quantity = 1,
				ReplacementNote = dto.CustomerNotes,
				CreatedAt = TimeHelper.GetEgyptTime()
			});
		}
		_db.ReturnExchangeRequests.Add(request);
		await _db.SaveChangesAsync();
		try
		{
			if (_notificationService != null)
			{
				string typeLabel = ((reqType == ReturnExchangeType.Exchange) ? "استبدال" : "استرجاع");
				Customer customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync((Customer c) => c.Id == customerId);
				string value3 = ((customer != null && !string.IsNullOrWhiteSpace(customer.FullName)) ? customer.FullName : "عميل المتجر");
				await _notificationService.SendAsync(null, "طلب " + typeLabel + " جديد \ud83d\udd04", $"New {reqType} Request", $"قام العميل ({value3}) بتقديم طلب {typeLabel} جديد للفاتورة رقم #{order.OrderNumber}", $"Customer ({value3}) submitted a {reqType} request for order #{order.OrderNumber}", "ReturnExchangeRequest", request.Id);
			}
		}
		catch
		{
		}
		try
		{
			await _hubContext.Clients.All.SendAsync("DashboardUpdate", new
			{
				type = "ReturnExchangeRequest",
				id = request.Id
			});
			await _hubContext.Clients.All.SendAsync("DashboardUpdated", new
			{
				type = "ReturnExchangeRequest",
				id = request.Id
			});
		}
		catch
		{
		}
		return Ok(new
		{
			message = "تم تقديم طلب الاستبدال بنجاح وسيتم مراجعته والتواصل معكم من الإدارة. ⏳",
			requestId = request.Id,
			isDeletedDirectly = false
		});
	}

	[HttpGet("my-return-exchange-requests")]
	public async Task<IActionResult> GetMyRequests()
	{
		int customerId = (await GetCurrentCustomerAsync())?.Id ?? 0;
		string s = base.User.FindFirst("CustomerId")?.Value;
		if (int.TryParse(s, out var result) && result > 0 && customerId == 0)
		{
			customerId = result;
		}
		List<ReturnExchangeRequestResponseDto> value = (await (from r in _db.ReturnExchangeRequests.AsNoTracking().Include((ReturnExchangeRequest r) => r.Order).Include((ReturnExchangeRequest r) => r.Items)
				.ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			where r.CustomerId == (int?)customerId || (r.Order != null && r.Order.CustomerId == customerId)
			orderby r.CreatedAt descending
			select r).ToListAsync()).Select(MapToResponseDto).ToList();
		return Ok(value);
	}

	private async Task<Customer?> GetCurrentCustomerAsync()
	{
		string text = base.User.FindFirst("CustomerId")?.Value;
		if (!string.IsNullOrEmpty(text) && int.TryParse(text, out var cId))
		{
			Customer customer = await _db.Customers.FirstOrDefaultAsync((Customer c) => c.Id == cId);
			if (customer != null)
			{
				return customer;
			}
		}
		string userIdStr = base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
		string userEmail = base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
		if (!string.IsNullOrEmpty(userEmail))
		{
			Customer customer2 = await _db.Customers.FirstOrDefaultAsync((Customer c) => c.Email == userEmail);
			if (customer2 != null)
			{
				return customer2;
			}
		}
		if (!string.IsNullOrEmpty(userIdStr))
		{
			Customer customer3 = await _db.Customers.FirstOrDefaultAsync((Customer c) => c.Email == userIdStr);
			if (customer3 != null)
			{
				return customer3;
			}
		}
		return null;
	}

	[HttpGet("admin-return-exchange-requests")]
	public async Task<IActionResult> GetAdminRequests([FromQuery] ReturnExchangeRequestListFilterDto filter)
	{
		IQueryable<ReturnExchangeRequest> source = _db.ReturnExchangeRequests.AsNoTracking().Include((ReturnExchangeRequest r) => r.Order).ThenInclude((Order o) => o.Customer)
			.Include((ReturnExchangeRequest r) => r.Customer)
			.Include((ReturnExchangeRequest r) => r.Items)
			.ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			.AsQueryable();
		if (!string.IsNullOrEmpty(filter.Type) && filter.Type != "all")
		{
			if (Enum.TryParse<ReturnExchangeType>(filter.Type, ignoreCase: true, out var parsedType))
			{
				source = source.Where((ReturnExchangeRequest r) => (int)r.Type == (int)parsedType);
			}
		}
		if (!string.IsNullOrEmpty(filter.Status) && filter.Status != "all")
		{
			if (Enum.TryParse<ReturnExchangeStatus>(filter.Status, ignoreCase: true, out var parsedStatus))
			{
				source = source.Where((ReturnExchangeRequest r) => (int)r.Status == (int)parsedStatus);
			}
		}
		if (!string.IsNullOrEmpty(filter.Search))
		{
			string s = filter.Search.Trim().ToLower();
			string searchHash = Customer.EncryptionHelper?.ComputeSearchHash(filter.Search.Trim()) ?? "";
			source = source.Where((ReturnExchangeRequest r) => (r.Order != null && r.Order.OrderNumber.ToLower().Contains(s)) || (r.Customer != null && r.Customer.FullName != null && r.Customer.FullName.ToLower().Contains(s)) || (!string.IsNullOrEmpty(searchHash) && r.Customer != null && r.Customer.PhoneHash == searchHash) || (r.Reason != null && r.Reason.ToLower().Contains(s)) || (r.CustomerNotes != null && r.CustomerNotes.ToLower().Contains(s)) || r.OrderId.ToString() == s || r.Id.ToString() == s);
		}
		List<ReturnExchangeRequest> list = await source.OrderByDescending((ReturnExchangeRequest r) => r.CreatedAt).ToListAsync();
		ReturnExchangeRequestSummaryDto summary = new ReturnExchangeRequestSummaryDto
		{
			Total = list.Count,
			Pending = list.Count((ReturnExchangeRequest r) => r.Status == ReturnExchangeStatus.Pending),
			Exchanges = list.Count((ReturnExchangeRequest r) => r.Type == ReturnExchangeType.Exchange),
			Returns = list.Count((ReturnExchangeRequest r) => r.Type == ReturnExchangeType.Return)
		};
		List<ReturnExchangeRequestResponseDto> items = list.Select(MapToResponseDto).ToList();
		return Ok(new ReturnExchangeRequestsPagedResultDto
		{
			Items = items,
			Summary = summary
		});
	}

	[HttpPost("return-exchange-requests/{requestId}/approve-exchange")]
	public async Task<IActionResult> ApproveExchange(int requestId)
	{
		ReturnExchangeRequest req = await _db.ReturnExchangeRequests.Include((ReturnExchangeRequest r) => r.Order).ThenInclude((Order o) => o.Items).Include((ReturnExchangeRequest r) => r.Items)
			.ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			.FirstOrDefaultAsync((ReturnExchangeRequest r) => r.Id == requestId);
		if (req == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		if (req.Type != ReturnExchangeType.Exchange)
		{
			return BadRequest("هذا الطلب ليس طلب استبدال.");
		}
		if (req.Status == ReturnExchangeStatus.Rejected)
		{
			return BadRequest("هذا الطلب مرفوض مسبقا\u064b.");
		}
		int targetWarehouseId = (req.Order?.WarehouseId).GetValueOrDefault();
		if (targetWarehouseId == 0)
		{
			targetWarehouseId = await (from w in _db.Warehouses
				where w.IsActive
				select w.Id).FirstOrDefaultAsync();
		}
		foreach (ReturnExchangeRequestItem reqItem in req.Items)
		{
			OrderItem orderItem = reqItem.OrderItem;
			if (orderItem == null || string.IsNullOrWhiteSpace(reqItem.ReplacementNote))
			{
				continue;
			}
			string text = reqItem.ReplacementNote.Trim();
			string productNamePart = text;
			string requestedColor = null;
			string requestedSize = null;
			if (productNamePart.StartsWith("بديل:"))
			{
				productNamePart = productNamePart.Substring("بديل:".Length).Trim();
			}
			else if (productNamePart.StartsWith("استبدال بمنتج:"))
			{
				productNamePart = productNamePart.Substring("استبدال بمنتج:".Length).Trim();
			}
			else if (productNamePart.StartsWith("استبدال بـ"))
			{
				productNamePart = productNamePart.Substring("استبدال بـ".Length).Trim();
			}
			else if (productNamePart.StartsWith("استبدال منتج:"))
			{
				productNamePart = productNamePart.Substring("استبدال منتج:".Length).Trim();
			}
			int num = productNamePart.IndexOf('(');
			if (num >= 0)
			{
				string text2 = productNamePart.Substring(num + 1).Replace(")", "").Trim();
				productNamePart = productNamePart.Substring(0, num).Trim();
				string[] array = text2.Split('|');
				string[] array2 = array;
				foreach (string text3 in array2)
				{
					string text4 = text3.Trim();
					if (text4.StartsWith("لون:"))
					{
						requestedColor = text4.Substring("لون:".Length).Trim();
					}
					else if (text4.StartsWith("بلون:"))
					{
						requestedColor = text4.Substring("بلون:".Length).Trim();
					}
					else if (text4.StartsWith("مقاس:"))
					{
						requestedSize = text4.Substring("مقاس:".Length).Trim();
					}
					else if (text4.StartsWith("بمقاس:"))
					{
						requestedSize = text4.Substring("بمقاس:".Length).Trim();
					}
				}
			}
			if (orderItem.ProductVariantId.HasValue)
			{
				ProductVariant oldVar = await _db.ProductVariants.FindAsync(orderItem.ProductVariantId.Value);
				if (oldVar != null)
				{
					oldVar.StockQuantity += reqItem.Quantity;
					oldVar.UpdatedAt = TimeHelper.GetEgyptTime();
					List<ProductWarehouseStock> list = await _db.ProductWarehouseStocks.Where((ProductWarehouseStock w) => w.ProductVariantId == oldVar.Id).ToListAsync();
					if (list.Any())
					{
						foreach (ProductWarehouseStock item in list)
						{
							item.Quantity = oldVar.StockQuantity;
							item.UpdatedAt = TimeHelper.GetEgyptTime();
						}
					}
				}
			}
			else if (orderItem.ProductId.HasValue)
			{
				Product product = await _db.Products.FindAsync(orderItem.ProductId.Value);
				if (product != null)
				{
					product.TotalStock += reqItem.Quantity;
					product.UpdatedAt = TimeHelper.GetEgyptTime();
				}
			}
			Product searchedProduct = await _db.Products.Include((Product p) => p.Variants).FirstOrDefaultAsync((Product p) => p.NameAr == productNamePart || p.NameEn == productNamePart || (productNamePart.Length > 3 && p.NameAr.Contains(productNamePart)) || (p.NameAr.Length > 3 && productNamePart.Contains(p.NameAr)));
			if (searchedProduct == null && orderItem.ProductId.HasValue)
			{
				searchedProduct = await _db.Products.Include((Product p) => p.Variants).FirstOrDefaultAsync((Product p) => p.Id == orderItem.ProductId.Value);
			}
			if (searchedProduct == null)
			{
				continue;
			}
			orderItem.ProductId = searchedProduct.Id;
			orderItem.ProductNameAr = searchedProduct.NameAr;
			orderItem.ProductNameEn = ((!string.IsNullOrEmpty(searchedProduct.NameEn)) ? searchedProduct.NameEn : searchedProduct.NameAr);
			orderItem.SKU = searchedProduct.SKU;
			ProductVariant matchedVariant = null;
			if (searchedProduct.Variants != null && searchedProduct.Variants.Any())
			{
				matchedVariant = searchedProduct.Variants.FirstOrDefault((ProductVariant v) => !string.IsNullOrEmpty(requestedColor) && ((v.ColorAr != null && v.ColorAr.Equals(requestedColor, StringComparison.OrdinalIgnoreCase)) || (v.Color != null && v.Color.Equals(requestedColor, StringComparison.OrdinalIgnoreCase))) && !string.IsNullOrEmpty(requestedSize) && v.Size != null && v.Size.Equals(requestedSize, StringComparison.OrdinalIgnoreCase)) ?? searchedProduct.Variants.FirstOrDefault((ProductVariant v) => (!string.IsNullOrEmpty(requestedColor) && ((v.ColorAr != null && v.ColorAr.Equals(requestedColor, StringComparison.OrdinalIgnoreCase)) || (v.Color != null && v.Color.Equals(requestedColor, StringComparison.OrdinalIgnoreCase)))) || (!string.IsNullOrEmpty(requestedSize) && v.Size != null && v.Size.Equals(requestedSize, StringComparison.OrdinalIgnoreCase))) ?? searchedProduct.Variants.FirstOrDefault();
			}
			if (matchedVariant != null)
			{
				matchedVariant.StockQuantity = Math.Max(0, matchedVariant.StockQuantity - reqItem.Quantity);
				matchedVariant.UpdatedAt = TimeHelper.GetEgyptTime();
				List<ProductWarehouseStock> list2 = await _db.ProductWarehouseStocks.Where((ProductWarehouseStock w) => w.ProductVariantId == matchedVariant.Id).ToListAsync();
				if (list2.Any())
				{
					foreach (ProductWarehouseStock item2 in list2)
					{
						item2.Quantity = matchedVariant.StockQuantity;
						item2.UpdatedAt = TimeHelper.GetEgyptTime();
					}
				}
				else if (targetWarehouseId > 0)
				{
					_db.ProductWarehouseStocks.Add(new ProductWarehouseStock
					{
						ProductVariantId = matchedVariant.Id,
						WarehouseId = targetWarehouseId,
						Quantity = matchedVariant.StockQuantity,
						CreatedAt = TimeHelper.GetEgyptTime()
					});
				}
				orderItem.ProductVariantId = matchedVariant.Id;
				orderItem.Color = ((!string.IsNullOrEmpty(matchedVariant.ColorAr)) ? matchedVariant.ColorAr : (matchedVariant.Color ?? requestedColor));
				orderItem.Size = matchedVariant.Size ?? requestedSize;
			}
			else
			{
				searchedProduct.TotalStock = Math.Max(0, searchedProduct.TotalStock - reqItem.Quantity);
				searchedProduct.UpdatedAt = TimeHelper.GetEgyptTime();
				orderItem.ProductVariantId = null;
				orderItem.Color = requestedColor;
				orderItem.Size = requestedSize;
			}
			DateTime now = TimeHelper.GetEgyptTime();
			ProductDiscount productDiscount = await (from x in _db.ProductDiscounts.AsNoTracking()
				where (x.ProductId == (int?)searchedProduct.Id || (searchedProduct.CategoryId != (int?)null && x.CategoryId == searchedProduct.CategoryId) || (searchedProduct.BrandId != (int?)null && x.BrandId == searchedProduct.BrandId) || (x.ProductId == (int?)null && x.CategoryId == (int?)null && x.BrandId == (int?)null)) && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now
				where (int)x.ApplyTo == 0 || (int)x.ApplyTo == 1
				orderby (x.ProductId != (int?)null) ? 4 : ((x.CategoryId != (int?)null) ? 3 : ((x.BrandId != (int?)null) ? 2 : 1)) descending
				select x).FirstOrDefaultAsync();
			decimal price = searchedProduct.Price;
			decimal num3 = ((searchedProduct.DiscountPrice.HasValue && searchedProduct.DiscountPrice.Value > 0m && searchedProduct.DiscountPrice.Value < price) ? searchedProduct.DiscountPrice.Value : price);
			if (productDiscount != null)
			{
				if (productDiscount.DiscountType == DiscountType.Percentage && productDiscount.DiscountValue > 0m)
				{
					decimal num4 = price * (1m - productDiscount.DiscountValue / 100m);
					if (num4 < num3)
					{
						num3 = num4;
					}
				}
				else if (productDiscount.DiscountType == DiscountType.FixedAmount && productDiscount.DiscountValue > 0m)
				{
					decimal num5 = Math.Max(0m, price - productDiscount.DiscountValue);
					if (num5 < num3)
					{
						num3 = num5;
					}
				}
			}
			decimal valueOrDefault = (matchedVariant?.PriceAdjustment).GetValueOrDefault();
			decimal num6 = price + valueOrDefault;
			decimal num7 = num3 + valueOrDefault;
			orderItem.OriginalUnitPrice = num6;
			orderItem.UnitPrice = num7;
			orderItem.DiscountAmount = Math.Max(0m, (num6 - num7) * (decimal)reqItem.Quantity);
			orderItem.TotalPrice = num7 * (decimal)reqItem.Quantity;
			if (orderItem.HasTax && orderItem.VatRateApplied.HasValue && orderItem.VatRateApplied.Value > 0m)
			{
				orderItem.ItemVatAmount = orderItem.TotalPrice * orderItem.VatRateApplied.Value / 100m;
			}
		}
		if (req.Order != null)
		{
			decimal num8 = req.Order.Items.Sum((OrderItem i) => ((i.OriginalUnitPrice > 0m) ? i.OriginalUnitPrice : i.UnitPrice) * (decimal)i.Quantity);
			decimal temporalDiscount = req.Order.Items.Sum((OrderItem i) => i.DiscountAmount);
			decimal num9 = req.Order.Items.Sum((OrderItem i) => i.ItemVatAmount);
			req.Order.SubTotal = num8;
			req.Order.TemporalDiscount = temporalDiscount;
			req.Order.TotalVatAmount = num9;
			req.Order.TotalAmount = Math.Max(0m, num8 - req.Order.DiscountAmount - req.Order.TemporalDiscount + req.Order.DeliveryFee + num9);
			req.Order.AdminNotes = req.Order.AdminNotes + $" | [تم تنفيذ الاستبدال وحفظ الصنف الجديد #{req.Id}]";
			req.Order.UpdatedAt = TimeHelper.GetEgyptTime();
		}
		req.Status = ReturnExchangeStatus.Completed;
		req.AdminNotes = $"[تمت الموافقة على الاستبدال وتحديث الصنف بالفاتورة والمخزن بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
		await _db.SaveChangesAsync();
		if (req.Order != null && _accounting != null)
		{
			try
			{
				await _accounting.PostSalesOrderAsync(req.Order);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to sync sales journal entry for order #{OrderNumber}", req.Order.OrderNumber);
			}
		}
		try
		{
			if (_notificationService != null)
			{
				await _notificationService.SendAsync(req.CustomerId?.ToString(), "تمت الموافقة على طلب الاستبدال \ud83c\udf89", "Exchange Request Approved", "تمت الموافقة على طلب الاستبدال للفاتورة #" + req.Order?.OrderNumber + " وتحديث الفاتورة بنجاح.", "Your exchange request for order #" + req.Order?.OrderNumber + " has been approved.", "Order", req.OrderId);
			}
		}
		catch
		{
		}
		try
		{
			await _hubContext.Clients.All.SendAsync("DashboardUpdate", new
			{
				type = "OrderUpdated",
				id = req.OrderId
			});
			await _hubContext.Clients.All.SendAsync("DashboardUpdated", new
			{
				type = "OrderUpdated",
				id = req.OrderId
			});
		}
		catch
		{
		}
		return Ok(new
		{
			message = "تمت الموافقة على الاستبدال وتحديث صنف الفاتورة والمخزن بنجاح \ud83c\udf1f"
		});
	}

	[HttpPost("return-exchange-requests/{requestId}/approve-return")]
	public async Task<IActionResult> ApproveReturn(int requestId)
	{
		ReturnExchangeRequest returnExchangeRequest = await _db.ReturnExchangeRequests
			.Include(r => r.Order)
				.ThenInclude(o => o.Items)
					.ThenInclude(i => i.Product)
			.Include(r => r.Order)
				.ThenInclude(o => o.Customer)
			.Include(r => r.Items)
				.ThenInclude(i => i.OrderItem)
					.ThenInclude(oi => oi.Product)
			.FirstOrDefaultAsync((ReturnExchangeRequest r) => r.Id == requestId);

		if (returnExchangeRequest == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		if (returnExchangeRequest.Type != ReturnExchangeType.Return)
		{
			return BadRequest("هذا الطلب ليس طلب استرجاع.");
		}
		if (returnExchangeRequest.Status != ReturnExchangeStatus.Pending)
		{
			return BadRequest("الطلب ليس في حالة قيد الانتظار.");
		}

		returnExchangeRequest.Status = ReturnExchangeStatus.Approved;
		returnExchangeRequest.AdminNotes = $"[موافقة تمهيدية - بانتظار استلام المرتجع بالمخزن {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
		await _db.SaveChangesAsync();

		// ── القيد المحاسبي: يُسجَّل فور الموافقة لأن البضاعة أصبحت رسمياً في طريقها للمخزن ──
		if (_accounting != null && returnExchangeRequest.Order != null)
		{
			try
			{
				bool isReturnedFromCourier = returnExchangeRequest.Order.Source != OrderSource.POS &&
					returnExchangeRequest.Order.Status >= OrderStatus.OutForDelivery;

				bool isManufacturingDefect = returnExchangeRequest.Reason?.Contains("عيب تصنيع") == true ||
					returnExchangeRequest.Reason?.Contains("صنف خطأ") == true ||
					returnExchangeRequest.Reason?.Contains("Manufacturing") == true ||
					returnExchangeRequest.Reason?.Contains("Wrong Item") == true;

				bool chargeReturnShipping = isReturnedFromCourier && !isManufacturingDefect;
				decimal returnShippingFee = chargeReturnShipping ? returnExchangeRequest.Order.DeliveryFee : 0;

				returnExchangeRequest.Order.Items = returnExchangeRequest.Items
					.Where(i => i.OrderItem != null)
					.Select(i =>
					{
						var orig = i.OrderItem;
						int qty = Math.Max(1, i.Quantity);
						return new OrderItem
						{
							Id = orig.Id,
							OrderId = orig.OrderId,
							ProductId = orig.ProductId,
							ProductVariantId = orig.ProductVariantId,
							ProductNameAr = orig.ProductNameAr,
							ProductNameEn = orig.ProductNameEn,
							Quantity = qty,
							UnitPrice = orig.UnitPrice,
							OriginalUnitPrice = orig.OriginalUnitPrice,
							DiscountAmount = orig.DiscountAmount,
							TotalPrice = qty * orig.UnitPrice,
							HasTax = orig.HasTax,
							VatRateApplied = orig.VatRateApplied,
							ItemVatAmount = orig.HasTax && orig.VatRateApplied.HasValue
								? (qty * orig.UnitPrice * orig.VatRateApplied.Value / 100m) : 0m,
							Product = orig.Product
						};
					}).ToList();

				await _accounting.PostSalesReturnAsync(
					returnExchangeRequest.Order,
					returnExchangeRequest.RefundAccountId,
					returnExchangeRequest.RefundShipping,
					chargeReturnShipping,
					returnShippingFee,
					isReturnedFromCourier
				);

				_logger.LogInformation(
					"[Accounting] Return entry posted for request #{RequestId} order #{OrderNumber} at ApproveReturn stage.",
					returnExchangeRequest.Id, returnExchangeRequest.Order.OrderNumber);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"[Accounting] Failed to post return entry for request #{RequestId} on order #{OrderNumber} at ApproveReturn.",
					returnExchangeRequest.Id, returnExchangeRequest.Order?.OrderNumber);
			}
		}

		return Ok(new
		{
			message = "تمت الموافقة المبدئية. في انتظار وصول المنتجات للمخزن."
		});
	}

	[HttpPost("return-exchange-requests/{requestId}/confirm-warehouse-receipt")]
	public async Task<IActionResult> ConfirmWarehouseReceipt(int requestId, [FromBody] ConfirmWarehouseReceiptDto dto)
	{
		ReturnExchangeRequest req = await _db.ReturnExchangeRequests.Include((ReturnExchangeRequest r) => r.Order).ThenInclude((Order o) => o.Items).ThenInclude((OrderItem i) => i.Product)
			.Include((ReturnExchangeRequest r) => r.Order)
			.ThenInclude((Order o) => o.Customer)
			.Include((ReturnExchangeRequest r) => r.Items)
			.ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			.ThenInclude((OrderItem oi) => oi.Product)
			.FirstOrDefaultAsync((ReturnExchangeRequest r) => r.Id == requestId);
		if (req == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		if (req.Status == ReturnExchangeStatus.ReceivedAtWarehouse || req.Status == ReturnExchangeStatus.Completed)
		{
			return BadRequest("تم تأكيد استلام هذا الطلب بالمخزن مسبقا\u064b.");
		}
		
		var originalStatus = req.Status; // Save status before modifying
		req.Status = ReturnExchangeStatus.ReceivedAtWarehouse;
		req.ReceivedAtWarehouseAt = TimeHelper.GetEgyptTime();
		req.RefundAccountId = dto.RefundAccountId;
		req.RefundShipping = dto.RefundShipping == true;
		if (!string.IsNullOrEmpty(dto.AdminNotes))
		{
			req.AdminNotes = req.AdminNotes + " | " + dto.AdminNotes;
		}
		decimal totalRefundValue = 0m;
		foreach (ReturnExchangeRequestItem item in req.Items)
		{
			OrderItem orderItem = item.OrderItem;
			if (orderItem == null)
			{
				continue;
			}
			orderItem.ReturnedQuantity += item.Quantity;
			totalRefundValue += orderItem.UnitPrice * (decimal)item.Quantity;
			int qtyToRestock = Math.Max(1, item.Quantity);
			if (_inventory != null)
			{
				bool flag = (req.Reason?.Contains("تالف") ?? false) || (req.Reason?.Contains("Damaged") ?? false);
				IInventoryService inventory = _inventory;
				decimal quantity = qtyToRestock;
				int? productId = orderItem.ProductId;
				int? productVariantId = orderItem.ProductVariantId;
				string orderNumber = req.Order.OrderNumber;
				string note = $"مرتجع شحنة بالمخزن - طلب استرجاع #{req.Id}";
				string? userId = base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
				OrderSource? costCenter = req.Order.Source;
				int? warehouseId = req.Order.WarehouseId;
				bool isDamaged = flag;
				await inventory.LogMovementAsync(InventoryMovementType.ReturnIn, quantity, productId, productVariantId, orderNumber, note, userId, 0m, costCenter, autoSave: false, broadcast: true, force: true, null, ignoreIdempotency: false, warehouseId, isDamaged);
			}
			else if (orderItem.ProductVariantId.HasValue)
			{
				ProductVariant productVariant = await _db.ProductVariants.FirstOrDefaultAsync((ProductVariant v) => v.Id == orderItem.ProductVariantId.Value);
				if (productVariant != null)
				{
					productVariant.StockQuantity += qtyToRestock;
				}
			}
			else if (orderItem.ProductId.HasValue)
			{
				Product product = await _db.Products.FirstOrDefaultAsync((Product p) => p.Id == orderItem.ProductId.Value);
				if (product != null)
				{
					product.TotalStock += qtyToRestock;
				}
			}
		}
		bool flag2 = req.Order.Items.All((OrderItem i) => i.ReturnedQuantity >= i.Quantity);
		OrderStatus status = (flag2 ? OrderStatus.Returned : OrderStatus.PartiallyReturned);
		req.Order.Status = status;
		req.Order.StatusHistory.Add(new OrderStatusHistory
		{
			OrderId = req.OrderId,
			Status = status,
			Note = (flag2 ? $"[مرتجع كامل]: تم تأكيد استلام الشحنة وإعادة الأصناف للمخزن (طلب استرجاع #{req.Id})" : $"[مرتجع جزئي]: تم تأكيد استلام المرتجع بالمخزن (طلب استرجاع #{req.Id})"),
			ChangedByUserId = (base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier") ?? "system"),
			CreatedAt = TimeHelper.GetEgyptTime()
		});
		try
		{
			if (_accounting != null && req.Order != null)
			{
				List<OrderItem> list = new List<OrderItem>();
				foreach (ReturnExchangeRequestItem item2 in req.Items)
				{
					if (item2.OrderItem != null)
					{
						OrderItem orderItem2 = item2.OrderItem;
						int num = Math.Max(1, item2.Quantity);
						list.Add(new OrderItem
						{
							Id = orderItem2.Id,
							OrderId = orderItem2.OrderId,
							ProductId = orderItem2.ProductId,
							ProductVariantId = orderItem2.ProductVariantId,
							ProductNameAr = orderItem2.ProductNameAr,
							ProductNameEn = orderItem2.ProductNameEn,
							Quantity = num,
							UnitPrice = orderItem2.UnitPrice,
							OriginalUnitPrice = orderItem2.OriginalUnitPrice,
							DiscountAmount = orderItem2.DiscountAmount,
							TotalPrice = (decimal)num * orderItem2.UnitPrice,
							HasTax = orderItem2.HasTax,
							VatRateApplied = orderItem2.VatRateApplied,
							ItemVatAmount = ((orderItem2.HasTax && orderItem2.VatRateApplied.HasValue) ? ((decimal)num * orderItem2.UnitPrice * orderItem2.VatRateApplied.Value / 100m) : 0m),
							Product = orderItem2.Product
						});
					}
				}
				bool isReturnedFromCourier = req.Order.Source != OrderSource.POS && req.Order.Status >= OrderStatus.OutForDelivery;
				bool isManufacturingDefect = (req.Reason?.Contains("عيب تصنيع") ?? false) || (req.Reason?.Contains("صنف خطأ") ?? false) || (req.Reason?.Contains("Manufacturing") ?? false) || (req.Reason?.Contains("Wrong Item") ?? false);
				bool chargeReturnShipping = isReturnedFromCourier && !isManufacturingDefect;
				decimal returnShippingFee = (chargeReturnShipping ? req.Order.DeliveryFee : 0m);
				
				// ── منع ازدواجية القيد المحاسبي ──
				// إذا كان الطلب قد مر بمرحلة الموافقة (Approved) وتم تسجيل القيد بالفعل، فلا تقم بتسجيله مرة أخرى هنا
				if (originalStatus != ReturnExchangeStatus.Approved)
				{
					if (flag2)
					{
						await _accounting.PostSalesReturnAsync(req.Order, dto.RefundAccountId, req.RefundShipping, chargeReturnShipping, returnShippingFee, isReturnedFromCourier);
					}
					else if (list.Any())
					{
						await _accounting.PostPartialSalesReturnAsync(req.Order, list, totalRefundValue, dto.RefundAccountId, refundToStoreCredit: false, $"{req.Order.OrderNumber}-RTN-{req.Id}", TimeHelper.GetEgyptTime());
					}
				}
				else
				{
					// تم إنشاء قيد مرتجع المبيعات مسبقاً (لحساب مخزن شركة الشحن)
					// نقوم الآن بإنشاء قيد مخزني فقط لنقل بضاعة المرتجع من مخزن شركة الشحن إلى المخزن الرئيسي
					await _accounting.PostWarehouseReceiptFromCourierAsync(req.Order, req.Id);
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to post return accounting entry for request #{RequestId} on order #{OrderNumber}", req.Id, req.Order?.OrderNumber);
		}
		await _db.SaveChangesAsync();
		return Ok(new
		{
			message = "تم تأكيد وصول المرتجع للمخزن وتحديث المخزون والقيد المحاسبي بنجاح.",
			refundAmount = totalRefundValue,
			orderStatus = req.Order?.Status.ToString()
		});
	}

	[HttpPost("return-exchange-requests/{requestId}/reject")]
	public async Task<IActionResult> RejectRequest(int requestId, [FromBody] RejectReturnExchangeRequestDto dto)
	{
		ReturnExchangeRequest returnExchangeRequest = await _db.ReturnExchangeRequests.FirstOrDefaultAsync((ReturnExchangeRequest r) => r.Id == requestId);
		if (returnExchangeRequest == null)
		{
			return NotFound("الطلب غير موجود.");
		}
		returnExchangeRequest.Status = ReturnExchangeStatus.Rejected;
		returnExchangeRequest.RejectionReason = dto?.Reason ?? "مرفوض من قبل الإدارة";
		await _db.SaveChangesAsync();
		return Ok(new
		{
			message = "تم رفض الطلب."
		});
	}

	[HttpPost("reprocess-return-request/{requestId}")]
	[Authorize(Policy = "AdminOnly")]
	public async Task<IActionResult> ReprocessReturnRequest(int requestId)
	{
		ReturnExchangeRequest req = await _db.ReturnExchangeRequests.Include((ReturnExchangeRequest r) => r.Order).ThenInclude((Order o) => o.Items).Include((ReturnExchangeRequest r) => r.Items)
			.ThenInclude((ReturnExchangeRequestItem i) => i.OrderItem)
			.FirstOrDefaultAsync((ReturnExchangeRequest r) => r.Id == requestId);
		if (req == null)
		{
			return NotFound("طلب المرتجع غير موجود.");
		}
		decimal totalRefundValue = 0m;
		foreach (ReturnExchangeRequestItem item in req.Items)
		{
			OrderItem orderItem = item.OrderItem;
			if (orderItem != null)
			{
				totalRefundValue += orderItem.UnitPrice * (decimal)item.Quantity;
				int num = Math.Max(1, item.Quantity);
				if (_inventory != null && req.Order != null)
				{
					IInventoryService inventory = _inventory;
					decimal quantity = num;
					int? productId = orderItem.ProductId;
					int? productVariantId = orderItem.ProductVariantId;
					string orderNumber = req.Order.OrderNumber;
					string note = $"إعادة مزامنة مرتجع بالمخزن - طلب #{req.Id}";
					string? userId = base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
					OrderSource? costCenter = req.Order.Source;
					int? warehouseId = req.Order.WarehouseId;
					await inventory.LogMovementAsync(InventoryMovementType.ReturnIn, quantity, productId, productVariantId, orderNumber, note, userId, 0m, costCenter, autoSave: false, broadcast: true, force: true, null, ignoreIdempotency: false, warehouseId);
				}
			}
		}
		req.Status = ReturnExchangeStatus.Completed;
		bool isFullReturn = req.Order != null && req.Order.Items.All((OrderItem i) => i.ReturnedQuantity >= i.Quantity);
		if (req.Order != null)
		{
			req.Order.Status = (isFullReturn ? OrderStatus.Returned : OrderStatus.PartiallyReturned);
			if (!isFullReturn && req.Order.PaymentMethod != PaymentMethod.Credit)
			{
				decimal num2 = req.Order.Items.Sum((OrderItem i) => i.UnitPrice * (decimal)i.ReturnedQuantity);
				req.Order.PaidAmount = Math.Max(0m, req.Order.TotalAmount - num2);
				req.Order.PaymentStatus = PaymentStatus.Paid;
			}
			if (!(await _db.OrderStatusHistories.AnyAsync((OrderStatusHistory h) => h.OrderId == req.OrderId && ((int)h.Status == 8 || (int)h.Status == 9))))
			{
				req.Order.StatusHistory.Add(new OrderStatusHistory
				{
					OrderId = req.OrderId,
					Status = req.Order.Status,
					Note = (isFullReturn ? $"[مرتجع كامل]: تم تأكيد استلام الشحنة وإعادة الأصناف للمخزن (طلب استرجاع #{req.Id})" : $"[مرتجع جزئي]: تم تأكيد استلام المرتجع بالمخزن (طلب استرجاع #{req.Id})"),
					ChangedByUserId = (base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier") ?? "system"),
					CreatedAt = TimeHelper.GetEgyptTime()
				});
			}
			try
			{
				if (_accounting != null)
				{
					List<OrderItem> list = new List<OrderItem>();
					foreach (ReturnExchangeRequestItem item2 in req.Items)
					{
						if (item2.OrderItem != null)
						{
							OrderItem orderItem2 = item2.OrderItem;
							int num3 = Math.Max(1, item2.Quantity);
							list.Add(new OrderItem
							{
								Id = orderItem2.Id,
								OrderId = orderItem2.OrderId,
								ProductId = orderItem2.ProductId,
								ProductVariantId = orderItem2.ProductVariantId,
								ProductNameAr = orderItem2.ProductNameAr,
								ProductNameEn = orderItem2.ProductNameEn,
								Quantity = num3,
								UnitPrice = orderItem2.UnitPrice,
								OriginalUnitPrice = orderItem2.OriginalUnitPrice,
								DiscountAmount = orderItem2.DiscountAmount,
								TotalPrice = (decimal)num3 * orderItem2.UnitPrice,
								HasTax = orderItem2.HasTax,
								VatRateApplied = orderItem2.VatRateApplied,
								ItemVatAmount = ((orderItem2.HasTax && orderItem2.VatRateApplied.HasValue) ? ((decimal)num3 * orderItem2.UnitPrice * orderItem2.VatRateApplied.Value / 100m) : 0m),
								Product = orderItem2.Product
							});
						}
					}
					if (isFullReturn)
					{
						await _accounting.PostSalesReturnAsync(req.Order, null, req.RefundShipping);
					}
					else if (list.Any())
					{
						await _accounting.PostPartialSalesReturnAsync(req.Order, list, totalRefundValue, null, refundToStoreCredit: false, $"{req.Order.OrderNumber}-RTN-{req.Id}", TimeHelper.GetEgyptTime());
					}
				}
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to reprocess return accounting entry for request #{RequestId}", req.Id);
			}
		}
		await _db.SaveChangesAsync();
		return Ok(new
		{
			message = $"تمت إعادة مزامنة القيد اليومي وحركة المخزون لطلب المرتجع #{requestId} بنجاح."
		});
	}

	private static ReturnExchangeRequestResponseDto MapToResponseDto(ReturnExchangeRequest r)
	{
		IEnumerable<string> values = r.Items.Select(delegate(ReturnExchangeRequestItem i)
		{
			string value = ((i.OrderItem != null) ? i.OrderItem.ProductNameAr : "منتج");
			string value2 = ((i.OrderItem != null && !string.IsNullOrEmpty(i.OrderItem.Color)) ? (" (اللون الحالي: " + i.OrderItem.Color + ")") : "");
			string value3 = ((i.OrderItem != null && !string.IsNullOrEmpty(i.OrderItem.Size)) ? (" (المقاس الحالي: " + i.OrderItem.Size + ")") : "");
			string value4 = ((!string.IsNullOrWhiteSpace(i.ReplacementNote)) ? (" ➔ [البديل المطلوب: " + i.ReplacementNote + "]") : "");
			return $"{value}{value2}{value3} × {i.Quantity}{value4}";
		});
		return new ReturnExchangeRequestResponseDto
		{
			Id = r.Id,
			OrderId = r.OrderId,
			OrderNumber = ((r.Order != null) ? r.Order.OrderNumber : ""),
			CustomerId = (r.CustomerId ?? r.Order?.CustomerId ?? 0),
			CustomerName = ((r.Customer != null && !string.IsNullOrWhiteSpace(r.Customer.FullName)) ? r.Customer.FullName : ((r.Order?.Customer != null && !string.IsNullOrWhiteSpace(r.Order.Customer.FullName)) ? r.Order.Customer.FullName : "عميل")),
			CustomerPhone = ((r.Customer != null && !string.IsNullOrWhiteSpace(r.Customer.Phone)) ? r.Customer.Phone : ((r.Order?.Customer != null && !string.IsNullOrWhiteSpace(r.Order.Customer.Phone)) ? r.Order.Customer.Phone : "")),
			Type = r.Type.ToString(),
			Status = r.Status.ToString(),
			Reason = r.Reason,
			CustomerNotes = r.CustomerNotes,
			AdminNotes = r.AdminNotes,
			RejectionReason = r.RejectionReason,
			ItemSummary = string.Join(" | ", values),
			CreatedAt = r.CreatedAt,
			ReceivedAtWarehouseAt = r.ReceivedAtWarehouseAt,
			Items = r.Items.Select((ReturnExchangeRequestItem i) => new ReturnExchangeRequestItemResponseDto
			{
				Id = i.Id,
				OrderItemId = i.OrderItemId,
				ProductName = ((i.OrderItem != null) ? i.OrderItem.ProductNameAr : ""),
				Size = i.OrderItem?.Size,
				Color = i.OrderItem?.Color,
				Quantity = i.Quantity,
				UnitPrice = (i.OrderItem?.UnitPrice ?? 0m),
				TotalPrice = (i.OrderItem?.UnitPrice ?? 0m) * (decimal)i.Quantity,
				ReplacementNote = i.ReplacementNote
			}).ToList()
		};
	}
}
