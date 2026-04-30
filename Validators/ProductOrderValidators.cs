// ============================================================
// Validators/ProductOrderValidators.cs
// ✅ تحسين: توسيع تغطية الـ Validation للمنتجات والطلبات
// ============================================================
using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

// ── PRODUCT ──────────────────────────────────────────────────
public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator(ITranslator translator)
    {
        RuleFor(x => x.NameAr)
            .NotEmpty().WithMessage(translator.Get("Products.NameArRequired"))
            .MaximumLength(200).WithMessage(translator.Get("Products.NameMaxLength"));

        RuleFor(x => x.NameEn)
            .NotEmpty().WithMessage(translator.Get("Products.NameEnRequired"))
            .MaximumLength(200);

        RuleFor(x => x.SKU)
            .NotEmpty().WithMessage(translator.Get("Products.SKURequired"))
            .MaximumLength(50)
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage(translator.Get("Products.SKUInvalid"));

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage(translator.Get("Products.PricePositive"));

        RuleFor(x => x.DiscountPrice)
            .LessThan(x => x.Price).When(x => x.DiscountPrice.HasValue && x.DiscountPrice > 0)
            .WithMessage(translator.Get("Products.DiscountPriceTooHigh"));

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).When(x => x.CostPrice.HasValue)
            .WithMessage(translator.Get("Products.CostPriceNegative"))
            .LessThanOrEqualTo(x => x.Price).When(x => x.CostPrice.HasValue)
            .WithMessage(translator.Get("Products.CostPriceTooHigh"));

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage(translator.Get("Products.CategoryRequired"));

        RuleFor(x => x.ReorderLevel)
            .GreaterThanOrEqualTo(0).WithMessage(translator.Get("Products.ReorderLevelNegative"));

        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 100).When(x => x.VatRate.HasValue)
            .WithMessage(translator.Get("Products.VatRateInvalid"));
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductValidator(ITranslator translator)
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.DiscountPrice)
            .LessThan(x => x.Price)
            .When(x => x.DiscountPrice.HasValue && x.DiscountPrice > 0)
            .WithMessage(translator.Get("Products.DiscountPriceTooHigh"));
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

// ── ORDER ─────────────────────────────────────────────────────
public class CreateOrderValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderValidator(ITranslator translator)
    {
        RuleFor(x => x.FulfillmentType)
            .IsInEnum().WithMessage(translator.Get("Orders.FulfillmentTypeInvalid"));

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage(translator.Get("Orders.PaymentMethodInvalid"));

        // إذا التوصيل للبيت، يجب تحديد عنوان
        RuleFor(x => x.DeliveryAddressId)
            .NotNull()
            .When(x => x.FulfillmentType == FulfillmentType.Delivery && x.Source == OrderSource.Website)
            .WithMessage(translator.Get("Orders.DeliveryAddressRequired"));

        // Items إجبارية للطلبات الإلكترونية
        RuleFor(x => x.Items)
            .NotNull().NotEmpty()
            .When(x => x.Source == OrderSource.Website)
            .WithMessage(translator.Get("Orders.ItemsRequired"));

        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemValidator(translator))
            .When(x => x.Items != null);
    }
}

public class CreateOrderItemValidator : AbstractValidator<CreateOrderItemDto>
{
    public CreateOrderItemValidator(ITranslator translator)
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage(translator.Get("Orders.ProductIdInvalid"));

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage(translator.Get("Orders.QuantityPositive"))
            .LessThanOrEqualTo(1000).WithMessage(translator.Get("Orders.QuantityTooHigh"));
    }
}

// ── CART ──────────────────────────────────────────────────────
public class AddToCartValidator : AbstractValidator<AddToCartDto>
{
    public AddToCartValidator(ITranslator translator)
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage(translator.Get("Cart.QuantityMin"))
            .LessThanOrEqualTo(100).WithMessage(translator.Get("Cart.QuantityMax"));
    }
}

// ── CUSTOMER ──────────────────────────────────────────────────
public class CreateCustomerValidator : AbstractValidator<CreateCustomerDto>
{
    public CreateCustomerValidator(ITranslator translator)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(translator.Get("Customers.FullNameRequired"))
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage(translator.Get("Customers.EmailInvalid"));

        RuleFor(x => x.Phone)
            .Matches(@"^(\+20|0)?1[0125]\d{8}$")
            .When(x => !string.IsNullOrEmpty(x.Phone))
            .WithMessage(translator.Get("Customers.PhoneInvalid"));
    }
}

// ── ADDRESS ───────────────────────────────────────────────────
public class CreateAddressValidator : AbstractValidator<CreateAddressDto>
{
    public CreateAddressValidator(ITranslator translator)
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue)
            .WithMessage(translator.Get("Addresses.LatitudeInvalid"));

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue)
            .WithMessage(translator.Get("Addresses.LongitudeInvalid"));
    }
}

// ── POS ───────────────────────────────────────────────────────
public class CreatePOSOrderValidator : AbstractValidator<CreatePOSOrderDto>
{
    public CreatePOSOrderValidator(ITranslator translator)
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage(translator.Get("POS.ItemsRequired"));
        RuleForEach(x => x.Items).ChildRules(items =>
        {
            items.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage(translator.Get("POS.QuantityPositive"));
            items.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0).WithMessage(translator.Get("POS.UnitPriceNegative"));
        });

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).When(x => x.DiscountAmount.HasValue)
            .WithMessage(translator.Get("POS.DiscountNegative"))
            .LessThanOrEqualTo(x => x.Subtotal).When(x => x.DiscountAmount.HasValue)
            .WithMessage(translator.Get("POS.DiscountTooHigh"));
        
        RuleFor(x => x.Subtotal)
            .GreaterThanOrEqualTo(0)
            .WithMessage(translator.Get("POS.SubtotalNegative"));

        RuleFor(x => x.PaidAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage(translator.Get("POS.PaidAmountNegative"))
            .LessThanOrEqualTo(x => x.Subtotal - (x.DiscountAmount ?? 0))
            .WithMessage(translator.Get("POS.PaidAmountTooHigh"));
    }
}
