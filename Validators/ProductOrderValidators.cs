using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Validators;

public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.NameAr)
            .NotEmpty().WithMessage("اسم المنتج بالعربي مطلوب")
            .MinimumLength(2).MaximumLength(200);

        RuleFor(x => x.NameEn)
            .NotEmpty().WithMessage("Product name in English is required")
            .MinimumLength(2).MaximumLength(200);

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("السعر لازم يكون أكبر من صفر");

        RuleFor(x => x.DiscountPrice)
            .GreaterThan(0).WithMessage("سعر الخصم لازم يكون أكبر من صفر")
            .LessThan(x => x.Price).WithMessage("سعر الخصم لازم يكون أقل من السعر الأصلي")
            .When(x => x.DiscountPrice.HasValue);

        RuleFor(x => x.SKU)
            .NotEmpty().WithMessage("كود المنتج (SKU) مطلوب")
            .MaximumLength(50)
            .Matches(@"^[0-9]+$").WithMessage("SKU يجب أن يحتوي على أرقام فقط");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("القسم مطلوب");

        RuleForEach(x => x.Variants)
            .SetValidator(new CreateVariantValidator())
            .When(x => x.Variants != null && x.Variants.Any());
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.DiscountPrice)
            .GreaterThan(0).LessThan(x => x.Price)
            .When(x => x.DiscountPrice.HasValue);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.SKU)
            .NotEmpty().WithMessage("كود المنتج (SKU) مطلوب")
            .MaximumLength(50)
            .Matches(@"^[0-9]+$").WithMessage("SKU يجب أن يحتوي على أرقام فقط");
    }
}

public class CreateVariantValidator : AbstractValidator<CreateVariantDto>
{
    public CreateVariantValidator()
    {
        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("المخزون لا يمكن أن يكون سالبًا");

        RuleFor(x => x.Size)
            .MaximumLength(20).WithMessage("المقاس لا يمكن أن يتجاوز 20 حرف")
            .When(x => !string.IsNullOrEmpty(x.Size));

        RuleFor(x => x.Color)
            .MaximumLength(50).WithMessage("اسم اللون لا يمكن أن يتجاوز 50 حرف")
            .When(x => !string.IsNullOrEmpty(x.Color));

        RuleFor(x => x.PriceAdjustment)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PriceAdjustment.HasValue);
    }
}

public class CreateCategoryValidator : AbstractValidator<CreateCategoryDto>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.NameEn).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum().WithMessage("نوع القسم غير صحيح");
    }
}

public class CreateOrderValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.FulfillmentType)
            .IsInEnum().WithMessage("نوع الاستلام غير صحيح");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("طريقة الدفع غير صحيحة");

        RuleFor(x => x.DeliveryAddressId)
            .NotNull().WithMessage("عنوان التوصيل مطلوب")
            .GreaterThan(0)
            .When(x => x.FulfillmentType == FulfillmentType.Delivery);

        RuleFor(x => x.PickupScheduledAt)
            .NotNull().WithMessage("وقت الاستلام مطلوب")
            .GreaterThan(DateTime.UtcNow).WithMessage("وقت الاستلام لازم يكون في المستقبل")
            .When(x => x.FulfillmentType == FulfillmentType.Pickup);

        RuleFor(x => x.CustomerNotes)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.CustomerNotes));
    }
}

public class AddToCartValidator : AbstractValidator<AddToCartDto>
{
    public AddToCartValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("المنتج غير صحيح");
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("الكمية لازم تكون أكبر من صفر")
            .LessThanOrEqualTo(50).WithMessage("لا يمكن إضافة أكثر من 50 قطعة");
    }
}

public class CreateAddressValidator : AbstractValidator<CreateAddressDto>
{
    public CreateAddressValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitude.HasValue);
    }
}
