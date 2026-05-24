using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

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
