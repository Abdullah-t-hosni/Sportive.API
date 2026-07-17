using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

public class CreateOrderValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderValidator(ITranslator translator)
    {
        RuleFor(x => x.FulfillmentType)
            .IsInEnum().WithMessage(translator.Get("Orders.FulfillmentTypeInvalid"));

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage(translator.Get("Orders.PaymentMethodInvalid"));

        RuleFor(x => x.DeliveryAddressId)
            .NotNull()
            .When(x => x.FulfillmentType == FulfillmentType.Delivery && (x.Source == OrderSource.Website || (int)x.Source == 0) && x.GuestAddress == null)
            .WithMessage(translator.Get("Orders.DeliveryAddressRequired"));

        RuleFor(x => x.Items)
            .NotNull().NotEmpty()
            .When(x => x.Source == OrderSource.Website || (int)x.Source == 0)
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
