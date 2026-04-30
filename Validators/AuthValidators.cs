using FluentValidation;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Validators;

public class RegisterValidator : AbstractValidator<RegisterDto>
{
    public RegisterValidator(ITranslator translator)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(translator.Get("Auth.FullNameRequired"))
            .MinimumLength(2).WithMessage(translator.Get("Auth.FullNameMinLength"))
            .MaximumLength(100).WithMessage(translator.Get("Auth.FullNameMaxLength"));

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(translator.Get("Auth.EmailRequired"))
            .EmailAddress().WithMessage(translator.Get("Auth.EmailInvalid"))
            .MaximumLength(100).WithMessage(translator.Get("Auth.EmailMaxLength"));

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(translator.Get("Auth.PasswordRequired"));

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage(translator.Get("Auth.PhoneRequired"))
            .Matches(@"^(\+20|0)?1[0125]\d{8}$")
            .WithMessage(translator.Get("Auth.PhoneInvalid"));
    }
}

public class LoginValidator : AbstractValidator<LoginDto>
{
    public LoginValidator(ITranslator translator)
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage(translator.Get("Auth.IdentifierRequired"));

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(translator.Get("Auth.PasswordRequired"));
    }
}

public class ChangePasswordValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordValidator(ITranslator translator)
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage(translator.Get("Auth.CurrentPasswordRequired"));
            
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(translator.Get("Auth.NewPasswordRequired"))
            .MinimumLength(6).WithMessage(translator.Get("Auth.NewPasswordMinLength"))
            .NotEqual(x => x.CurrentPassword).WithMessage(translator.Get("Auth.NewPasswordDifferent"));
    }
}
