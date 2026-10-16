using System.ComponentModel.DataAnnotations;

namespace Gaalsolutions.Monitoring.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Ingresa tu correo electrónico.")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Ingresa tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = "";

        [Display(Name = "Mantener la sesión iniciada")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}