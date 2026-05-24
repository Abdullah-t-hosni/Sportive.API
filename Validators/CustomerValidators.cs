using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

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

public class CreateAddressValidator : AbstractValidator<CreateAddressDto>
{
    public CreateAddressValidator(ITranslator translator)
    {
        RuleFor(x => x.TitleAr).MaximumLength(100);
        RuleFor(x => x.TitleEn).MaximumLength(100);
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
