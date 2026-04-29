using FluentValidation;
using Sportive.API.DTOs;

namespace Sportive.API.Validators;

public class RegisterValidator : AbstractValidator<RegisterDto>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("الاسم بالكامل مطلوب / Full name is required")
            .MinimumLength(2).WithMessage("الاسم لازم يكون 3 حروف على الأقل")
            .MaximumLength(100).WithMessage("الاسم لا يجب أن يتجاوز 100 حرف");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("البريد الإلكتروني مطلوب")
            .EmailAddress().WithMessage("صيغة البريد الإلكتروني غير صحيحة")
            .MaximumLength(100).WithMessage("البريد الإلكتروني لا يجب أن يتجاوز 100 حرف");

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
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("كلمة المرور الحالية مطلوبة");
            
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("كلمة المرور الجديدة مطلوبة")
            .MinimumLength(6).WithMessage("كلمة المرور الجديدة يجب أن تكون 6 أحرف على الأقل")
            .NotEqual(x => x.CurrentPassword).WithMessage("كلمة المرور الجديدة يجب أن تختلف عن القديمة");
    }
}
