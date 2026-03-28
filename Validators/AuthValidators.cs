using FluentValidation;
using Sportive.API.DTOs;

namespace Sportive.API.Validators;

public class RegisterValidator : AbstractValidator<RegisterDto>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("الاسم الأول مطلوب / First name is required")
            .MinimumLength(2).WithMessage("الاسم الأول لازم يكون 2 حروف على الأقل")
            .MaximumLength(50).WithMessage("الاسم الأول لا يتجاوز 50 حرف");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("اسم العائلة مطلوب / Last name is required")
            .MinimumLength(2).MaximumLength(50);

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("كلمة المرور مطلوبة");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("رقم الهاتف مطلوب / Phone is required")
            .Matches(@"^(\+20|0)?1[0125]\d{8}$")
            .WithMessage("رقم الهاتف غير صحيح (مثال: 01012345678)");
    }
}

public class LoginValidator : AbstractValidator<LoginDto>
{
    public LoginValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("يرجى إدخال الهاتف أو البريد الإلكتروني أو اسم المستخدم");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("كلمة المرور مطلوبة");
    }
}

public class ChangePasswordValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("كلمة المرور الجديدة لازم تختلف عن القديمة");
    }
}
