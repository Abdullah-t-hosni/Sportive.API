// ============================================================
// Validators/ProductOrderValidators.cs
// ✅ تحسين: توسيع تغطية الـ Validation للمنتجات والطلبات
// ============================================================
using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Validators;

// ── PRODUCT ──────────────────────────────────────────────────
public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.NameAr)
            .NotEmpty().WithMessage("اسم المنتج بالعربية مطلوب")
            .MaximumLength(200).WithMessage("الاسم لا يتجاوز 200 حرف");

        RuleFor(x => x.NameEn)
            .NotEmpty().WithMessage("Product English name is required")
            .MaximumLength(200);

        RuleFor(x => x.SKU)
            .NotEmpty().WithMessage("رمز SKU مطلوب")
            .MaximumLength(50)
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("SKU يجب أن يحتوي على حروف وأرقام وشرطات فقط");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("السعر يجب أن يكون أكبر من صفر");

        RuleFor(x => x.DiscountPrice)
            .LessThan(x => x.Price).When(x => x.DiscountPrice.HasValue && x.DiscountPrice > 0)
            .WithMessage("سعر الخصم يجب أن يكون أقل من السعر الأصلي");

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).When(x => x.CostPrice.HasValue)
            .WithMessage("سعر التكلفة لا يمكن أن يكون سالباً");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("يجب اختيار فئة للمنتج");

        RuleFor(x => x.ReorderLevel)
            .GreaterThanOrEqualTo(0).WithMessage("حد الطلب لا يمكن أن يكون سالباً");

        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 100).When(x => x.VatRate.HasValue)
            .WithMessage("نسبة الضريبة يجب أن تكون بين 0 و 100");
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.DiscountPrice)
            .LessThan(x => x.Price)
            .When(x => x.DiscountPrice.HasValue && x.DiscountPrice > 0)
            .WithMessage("سعر الخصم يجب أن يكون أقل من السعر الأصلي");
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

// ── ORDER ─────────────────────────────────────────────────────
public class CreateOrderValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.FulfillmentType)
            .IsInEnum().WithMessage("نوع التسليم غير صحيح");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("طريقة الدفع غير صحيحة");

        // إذا التوصيل للبيت، يجب تحديد عنوان
        RuleFor(x => x.DeliveryAddressId)
            .NotNull()
            .When(x => x.FulfillmentType == FulfillmentType.Delivery && x.Source == OrderSource.Website)
            .WithMessage("يجب اختيار عنوان التوصيل");

        // Items إجبارية للطلبات الإلكترونية
        RuleFor(x => x.Items)
            .NotNull().NotEmpty()
            .When(x => x.Source == OrderSource.Website)
            .WithMessage("يجب إضافة منتجات للطلب");

        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemValidator())
            .When(x => x.Items != null);
    }
}

public class CreateOrderItemValidator : AbstractValidator<CreateOrderItemDto>
{
    public CreateOrderItemValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("معرف المنتج غير صحيح");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر")
            .LessThanOrEqualTo(1000).WithMessage("الكمية كبيرة جداً — الحد الأقصى 1000");
    }
}

// ── CART ──────────────────────────────────────────────────────
public class AddToCartValidator : AbstractValidator<AddToCartDto>
{
    public AddToCartValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("الكمية يجب أن تكون 1 على الأقل")
            .LessThanOrEqualTo(100).WithMessage("الحد الأقصى لكمية واحدة هو 100");
    }
}

// ── CUSTOMER ──────────────────────────────────────────────────
public class CreateCustomerValidator : AbstractValidator<CreateCustomerDto>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("الاسم الأول مطلوب")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة");

        RuleFor(x => x.Phone)
            .Matches(@"^(\+20|0)?1[0125]\d{8}$")
            .When(x => !string.IsNullOrEmpty(x.Phone))
            .WithMessage("رقم الهاتف المصري غير صحيح (مثال: 01012345678)");
    }
}

// ── ADDRESS ───────────────────────────────────────────────────
public class CreateAddressValidator : AbstractValidator<CreateAddressDto>
{
    public CreateAddressValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue)
            .WithMessage("خط العرض يجب أن يكون بين -90 و 90");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue)
            .WithMessage("خط الطول يجب أن يكون بين -180 و 180");
    }
}
