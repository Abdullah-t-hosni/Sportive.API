// ============================================================
// Validators/BusinessValidators.cs
// Purchase, Supplier, Coupon, Inventory DTOs
// ============================================================
using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Validators;

// ── SUPPLIER ──────────────────────────────────────────────────

public class CreateSupplierValidator : AbstractValidator<CreateSupplierDto>
{
    public CreateSupplierValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("اسم المورد مطلوب")
            .MaximumLength(200).WithMessage("الاسم لا يتجاوز 200 حرف");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("رقم هاتف المورد مطلوب")
            .MaximumLength(20);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة");

        RuleFor(x => x.TaxNumber)
            .MaximumLength(50).When(x => x.TaxNumber != null)
            .WithMessage("الرقم الضريبي لا يتجاوز 50 حرفاً");
    }
}

public class UpdateSupplierValidator : AbstractValidator<UpdateSupplierDto>
{
    public UpdateSupplierValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("اسم المورد مطلوب")
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("رقم هاتف المورد مطلوب")
            .MaximumLength(20);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة");
    }
}

// ── PURCHASE INVOICE ──────────────────────────────────────────

public class CreatePurchaseInvoiceValidator : AbstractValidator<CreatePurchaseInvoiceDto>
{
    public CreatePurchaseInvoiceValidator()
    {
        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage("يجب اختيار مورد صحيح");

        RuleFor(x => x.InvoiceDate)
            .NotEmpty().WithMessage("تاريخ الفاتورة مطلوب")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .WithMessage("تاريخ الفاتورة لا يمكن أن يكون في المستقبل");

        RuleFor(x => x.TaxPercent)
            .InclusiveBetween(0, 100).WithMessage("نسبة الضريبة يجب أن تكون بين 0 و 100");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage("قيمة الخصم لا يمكن أن تكون سالبة");

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage("يجب إضافة بند واحد على الأقل للفاتورة");

        RuleForEach(x => x.Items)
            .SetValidator(new CreatePurchaseItemValidator());
    }
}

public class CreatePurchaseItemValidator : AbstractValidator<CreatePurchaseItemDto>
{
    public CreatePurchaseItemValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("وصف البند مطلوب")
            .MaximumLength(500);

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر");

        RuleFor(x => x.UnitCost)
            .GreaterThanOrEqualTo(0).WithMessage("تكلفة الوحدة لا يمكن أن تكون سالبة");
    }
}

public class UpdatePurchaseInvoiceValidator : AbstractValidator<UpdatePurchaseInvoiceDto>
{
    public UpdatePurchaseInvoiceValidator()
    {
        RuleFor(x => x.InvoiceDate)
            .NotEmpty().WithMessage("تاريخ الفاتورة مطلوب");

        RuleFor(x => x.TaxPercent)
            .InclusiveBetween(0, 100).WithMessage("نسبة الضريبة يجب أن تكون بين 0 و 100");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage("قيمة الخصم لا يمكن أن تكون سالبة");

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage("يجب إضافة بند واحد على الأقل");

        RuleForEach(x => x.Items)
            .SetValidator(new CreatePurchaseItemValidator());
    }
}

// ── SUPPLIER PAYMENT ─────────────────────────────────────────

public class CreateSupplierPaymentValidator : AbstractValidator<CreateSupplierPaymentDto>
{
    public CreateSupplierPaymentValidator()
    {
        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage("يجب اختيار مورد صحيح");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("قيمة الدفعة يجب أن تكون أكبر من صفر");

        RuleFor(x => x.PaymentDate)
            .NotEmpty().WithMessage("تاريخ الدفعة مطلوب")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .WithMessage("تاريخ الدفعة لا يمكن أن يكون في المستقبل");

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("اسم حساب الدفع مطلوب")
            .MaximumLength(100);

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("طريقة الدفع غير صحيحة");
    }
}

// ── COUPON ───────────────────────────────────────────────────

public class CreateCouponValidator : AbstractValidator<CreateCouponDto>
{
    public CreateCouponValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("كود الكوبون مطلوب")
            .MaximumLength(50).WithMessage("كود الكوبون لا يتجاوز 50 حرفاً")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("كود الكوبون يجب أن يحتوي على حروف وأرقام فقط");

        RuleFor(x => x.DiscountType)
            .IsInEnum().WithMessage("نوع الخصم غير صحيح");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("قيمة الخصم يجب أن تكون أكبر من صفر");

        // For percentage coupons the value must be ≤ 100
        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage("نسبة الخصم لا يمكن أن تتجاوز 100%");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue)
            .WithMessage("الحد الأدنى للطلب لا يمكن أن يكون سالباً");

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThan(0).When(x => x.MaxDiscountAmount.HasValue)
            .WithMessage("الحد الأقصى للخصم يجب أن يكون أكبر من صفر");

        RuleFor(x => x.MaxUsageCount)
            .GreaterThan(0).When(x => x.MaxUsageCount.HasValue)
            .WithMessage("عدد مرات الاستخدام يجب أن يكون أكبر من صفر");

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).When(x => x.ExpiresAt.HasValue)
            .WithMessage("تاريخ انتهاء الكوبون يجب أن يكون في المستقبل");
    }
}

// ── INVENTORY AUDIT ──────────────────────────────────────────

public class CreateInventoryAuditValidator : AbstractValidator<CreateInventoryAuditDto>
{
    public CreateInventoryAuditValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("عنوان الجرد مطلوب")
            .MaximumLength(200).WithMessage("العنوان لا يتجاوز 200 حرف");

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage("يجب إضافة بند واحد على الأقل للجرد");

        RuleForEach(x => x.Items)
            .SetValidator(new CreateInventoryAuditItemValidator());
    }
}

public class UpdateInventoryAuditValidator : AbstractValidator<UpdateInventoryAuditDto>
{
    public UpdateInventoryAuditValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("عنوان الجرد مطلوب")
            .MaximumLength(200);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("حالة الجرد غير صحيحة");

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage("يجب إضافة بند واحد على الأقل للجرد");

        RuleForEach(x => x.Items)
            .SetValidator(new CreateInventoryAuditItemValidator());
    }
}

public class CreateInventoryAuditItemValidator : AbstractValidator<CreateInventoryAuditItemDto>
{
    public CreateInventoryAuditItemValidator()
    {
        RuleFor(x => x)
            .Must(x => x.ProductId.HasValue || x.ProductVariantId.HasValue)
            .WithMessage("يجب تحديد منتج أو موديل للبند");

        RuleFor(x => x.ActualQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("الكمية الفعلية لا يمكن أن تكون سالبة");
    }
}
