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
            .NotEmpty().WithMessage("كلمة المرور مطلوبة")
            .MinimumLength(8).WithMessage("كلمة المرور لازم تكون 8 أحرف على الأقل")
            .Matches("[A-Z]").WithMessage("لازم تحتوي على حرف كبير")
            .Matches("[0-9]").WithMessage("لازم تحتوي على رقم");

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
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("رقم الهاتف مطلوب / Phone is required")
            .Matches(@"^(\+20|0)?1[0125]\d{8}$")
            .WithMessage("رقم الهاتف غير صحيح (مثال: 01012345678)");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة");

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
            .MinimumLength(8)
            .Matches("[A-Z]")
            .Matches("[0-9]")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("كلمة المرور الجديدة لازم تختلف عن القديمة");
    }
}
