// ============================================================
// Validators/BusinessValidators.cs
// Purchase, Supplier, Coupon, Inventory DTOs
// ============================================================
using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

// ── SUPPLIER ──────────────────────────────────────────────────

public class CreateSupplierValidator : AbstractValidator<CreateSupplierDto>
{
    public CreateSupplierValidator(ITranslator translator)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(translator.Get("Suppliers.NameRequired"))
            .MaximumLength(200).WithMessage(translator.Get("Suppliers.NameMaxLength"));

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage(translator.Get("Suppliers.PhoneRequired"))
            .MaximumLength(20);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage(translator.Get("Suppliers.EmailInvalid"));

        RuleFor(x => x.TaxNumber)
            .MaximumLength(50).When(x => x.TaxNumber != null)
            .WithMessage(translator.Get("Suppliers.TaxNumberMaxLength"));
    }
}

public class UpdateSupplierValidator : AbstractValidator<UpdateSupplierDto>
{
    public UpdateSupplierValidator(ITranslator translator)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(translator.Get("Suppliers.NameRequired"))
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage(translator.Get("Suppliers.PhoneRequired"))
            .MaximumLength(20);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage(translator.Get("Suppliers.EmailInvalid"));
    }
}

// ── PURCHASE INVOICE ──────────────────────────────────────────

public class CreatePurchaseInvoiceValidator : AbstractValidator<CreatePurchaseInvoiceDto>
{
    public CreatePurchaseInvoiceValidator(ITranslator translator)
    {
        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage(translator.Get("Purchases.SupplierRequired"));

        RuleFor(x => x.InvoiceDate)
            .NotEmpty().WithMessage(translator.Get("Purchases.DateRequired"))
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .WithMessage(translator.Get("Purchases.DateInFuture"));

        RuleFor(x => x.TaxPercent)
            .InclusiveBetween(0, 100).WithMessage(translator.Get("Purchases.TaxPercentInvalid"));

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage(translator.Get("Purchases.DiscountNegative"))
            .LessThanOrEqualTo(x => x.Items != null ? x.Items.Sum(i => i.Quantity * i.UnitCost) : 0)
            .WithMessage(translator.Get("Purchases.DiscountTooHigh"));

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage(translator.Get("Purchases.ItemsRequired"));

        RuleForEach(x => x.Items)
            .SetValidator(new CreatePurchaseItemValidator(translator));
    }
}

public class CreatePurchaseItemValidator : AbstractValidator<CreatePurchaseItemDto>
{
    public CreatePurchaseItemValidator(ITranslator translator)
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage(translator.Get("Purchases.ItemDescriptionRequired"))
            .MaximumLength(500);

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage(translator.Get("Purchases.ItemQuantityPositive"));

        RuleFor(x => x.UnitCost)
            .GreaterThanOrEqualTo(0).WithMessage(translator.Get("Purchases.ItemCostNegative"));
    }
}

public class UpdatePurchaseInvoiceValidator : AbstractValidator<UpdatePurchaseInvoiceDto>
{
    public UpdatePurchaseInvoiceValidator(ITranslator translator)
    {
        RuleFor(x => x.InvoiceDate)
            .NotEmpty().WithMessage(translator.Get("Purchases.DateRequired"));

        RuleFor(x => x.TaxPercent)
            .InclusiveBetween(0, 100).WithMessage(translator.Get("Purchases.TaxPercentInvalid"));

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage(translator.Get("Purchases.DiscountNegative"))
            .LessThanOrEqualTo(x => x.Items != null ? x.Items.Sum(i => i.Quantity * i.UnitCost) : 0)
            .WithMessage(translator.Get("Purchases.DiscountTooHigh"));

        RuleFor(x => x.Items)
            .NotNull().NotEmpty().WithMessage(translator.Get("Purchases.ItemsRequired"));

        RuleForEach(x => x.Items)
            .SetValidator(new CreatePurchaseItemValidator(translator));
    }
}

// ── SUPPLIER PAYMENT ─────────────────────────────────────────

public class CreateSupplierPaymentValidator : AbstractValidator<CreateSupplierPaymentDto>
{
    public CreateSupplierPaymentValidator(ITranslator translator)
    {
        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage(translator.Get("Purchases.SupplierRequired"));

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage(translator.Get("Payments.AmountPositive"));

        RuleFor(x => x.PaymentDate)
            .NotEmpty().WithMessage(translator.Get("Payments.DateRequired"))
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .WithMessage(translator.Get("Payments.DateInFuture"));

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage(translator.Get("Payments.AccountNameRequired"))
            .MaximumLength(100);

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage(translator.Get("Payments.MethodInvalid"));
    }
}

// ── COUPON ───────────────────────────────────────────────────

public class CreateCouponValidator : AbstractValidator<CreateCouponDto>
{
    public CreateCouponValidator(ITranslator translator)
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage(translator.Get("Coupons.CodeRequired"))
            .MaximumLength(50).WithMessage(translator.Get("Coupons.CodeMaxLength"))
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage(translator.Get("Coupons.CodeInvalid"));

        RuleFor(x => x.DiscountType)
            .IsInEnum().WithMessage(translator.Get("Coupons.TypeInvalid"));

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage(translator.Get("Coupons.ValuePositive"));

        // For percentage coupons the value must be ≤ 100
        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage(translator.Get("Coupons.PercentageTooHigh"));

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue)
            .WithMessage(translator.Get("Coupons.MinOrderAmountNegative"));

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThan(0).When(x => x.MaxDiscountAmount.HasValue)
            .WithMessage(translator.Get("Coupons.MaxDiscountPositive"))
            .LessThanOrEqualTo(x => x.MinOrderAmount ?? decimal.MaxValue).When(x => x.MaxDiscountAmount.HasValue && x.MinOrderAmount.HasValue)
            .WithMessage(translator.Get("Coupons.MaxDiscountTooHigh"));

        RuleFor(x => x.MaxUsageCount)
            .GreaterThan(0).When(x => x.MaxUsageCount.HasValue)
            .WithMessage(translator.Get("Coupons.UsageCountPositive"));

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).When(x => x.ExpiresAt.HasValue)
            .WithMessage(translator.Get("Coupons.ExpiryInFuture"));
    }
}

// ── INVENTORY AUDIT ──────────────────────────────────────────

public class CreateInventoryAuditValidator : AbstractValidator<CreateInventoryAuditDto>
{
    public CreateInventoryAuditValidator(ITranslator translator)
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage(translator.Get("Inventory.TitleRequired"))
            .MaximumLength(200).WithMessage(translator.Get("Inventory.TitleMaxLength"));

        RuleFor(x => x.Items)
            .NotNull().WithMessage(translator.Get("Inventory.ItemsRequired"));

        RuleForEach(x => x.Items)
            .SetValidator(new CreateInventoryAuditItemValidator(translator));
    }
}

public class UpdateInventoryAuditValidator : AbstractValidator<UpdateInventoryAuditDto>
{
    public UpdateInventoryAuditValidator(ITranslator translator)
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage(translator.Get("Inventory.TitleRequired"))
            .MaximumLength(200);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage(translator.Get("Inventory.StatusInvalid"));

        RuleFor(x => x.Items)
            .NotNull().WithMessage(translator.Get("Inventory.ItemsRequired"));

        RuleForEach(x => x.Items)
            .SetValidator(new CreateInventoryAuditItemValidator(translator));
    }
}

public class CreateInventoryAuditItemValidator : AbstractValidator<CreateInventoryAuditItemDto>
{
    public CreateInventoryAuditItemValidator(ITranslator translator)
    {
        RuleFor(x => x)
            .Must(x => x.ProductId.HasValue || x.ProductVariantId.HasValue)
            .WithMessage(translator.Get("Inventory.ProductRequired"));

        RuleFor(x => x.ActualQuantity)
            .GreaterThanOrEqualTo(0).WithMessage(translator.Get("Inventory.QuantityNegative"))
            .LessThanOrEqualTo(1000000).WithMessage(translator.Get("Inventory.QuantityTooHigh"));
    }
}
