using ezZkvi.Models;
using ezZkvi.Data;
using Microsoft.EntityFrameworkCore;
using ezZkvi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ezZkvi.Services;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace ezZkvi.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly SignInManager<Korisnik> _signInManager;
        private readonly UserManager<Korisnik> _userManager;
        private readonly IEmailService _emailService;
        private readonly ApplicationDbContext _context;

        public AccountController(SignInManager<Korisnik> signInManager, UserManager<Korisnik> userManager,
        IEmailService emailService, ApplicationDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _emailService = emailService;
            _context = context;
        }

        private async Task<IActionResult> RedirectToDashboardByRole(Korisnik user)
        {
            if (await _userManager.IsInRoleAsync(user, "Admin"))
                return RedirectToAction("Dashboard", "Administrator");

            if (await _userManager.IsInRoleAsync(user, "Moderator"))
                return RedirectToAction("Dashboard", "Moderator");

            if (await _userManager.IsInRoleAsync(user, "Student"))
                return RedirectToAction("Dashboard", "Student");

            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/Login
        [HttpGet]
        public async Task<IActionResult> Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var user = await _userManager.GetUserAsync(User);

                if (user != null)
                    return await RedirectToDashboardByRole(user);

                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
            {
                ModelState.AddModelError("", "Pogrešan email ili lozinka.");
                return View(model);
            }

            if (!user.EmailConfirmed)
            {
                ModelState.AddModelError("", "Morate potvrditi email adresu prije prijave.");
                return View(model);
            }

            if (!user.IsApproved)
            {
                ModelState.AddModelError("", "Vaš nalog još nije odobren od strane administratora.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return await RedirectToDashboardByRole(user);
            }

            ModelState.AddModelError("", "Pogrešan email ili lozinka.");
            return View(model);
        }

        // GET: /Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            return RedirectToAction("Login");
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            ViewData["ActiveTab"] = "register";

            if (!ModelState.IsValid)
                return View("Login", model);

            var existingUser = await _userManager.FindByEmailAsync(model.Email);

            if (existingUser != null)
            {
                if (!existingUser.EmailConfirmed)
                {
                    try
                    {
                        var token = await _userManager.GenerateEmailConfirmationTokenAsync(existingUser);
                        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                        var confirmationUrl = Url.Action(
                            "ConfirmEmail",
                            "Account",
                            new
                            {
                                userId = existingUser.Id,
                                token = encodedToken
                            },
                            Request.Scheme
                        );

                        if (string.IsNullOrWhiteSpace(confirmationUrl))
                        {
                            throw new InvalidOperationException("Verifikacijski link nije mogao biti kreiran.");
                        }

                        var subject = "Potvrda email adrese za eZkvi";

                        var body = $@"
                                                    Poštovani,

                                                    Za završetak registracije potvrdite svoju email adresu putem ovog linka:

                                                    {confirmationUrl}

                                                    {(existingUser.IsApproved ? "Nakon potvrde emaila možete se prijaviti." : "Nakon potvrde emaila, vaš nalog će biti poslan administratoru na odobrenje.")}";

                        await _emailService.SendEmailAsync(existingUser.Email!, subject, body);

                        TempData["Message"] = "Novi verifikacijski link je poslan na email adresu.";
                        return RedirectToAction("Login", "Account");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("EMAIL VERIFICATION RESEND ERROR: " + ex.ToString());

                        ModelState.AddModelError("", "Novi verifikacijski email nije poslan. Pokušajte ponovo kasnije.");
                        return View("Login", model);
                    }
                }

                if (!existingUser.IsApproved)
                {
                    ModelState.AddModelError("", "Korisnik sa ovom email adresom je već registrovan i čeka odobrenje administratora.");
                    return View("Login", model);
                }

                ModelState.AddModelError("", "Korisnik sa ovom email adresom već postoji.");
                return View("Login", model);
            }

            var baseUsername = model.Email.Split('@')[0];
            var username = baseUsername;
            int suffix = 1;

            while (await _userManager.FindByNameAsync(username) != null)
            {
                username = baseUsername + suffix;
                suffix++;
            }

            var user = new Korisnik
            {
                UserName = username,
                Email = model.Email,
                EmailConfirmed = false,
                IsApproved = !await _context.ApplicationSettings.Where(s => s.Id == 1).Select(s => s.RequireAdminApproval).SingleAsync()
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                return View("Login", model);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Student");

            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);

                foreach (var error in roleResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                return View("Login", model);
            }

            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                var confirmationUrl = Url.Action(
                    "ConfirmEmail",
                    "Account",
                    new
                    {
                        userId = user.Id,
                        token = encodedToken
                    },
                    Request.Scheme
                );

                if (string.IsNullOrWhiteSpace(confirmationUrl))
                {
                    throw new InvalidOperationException("Verifikacijski link nije mogao biti kreiran.");
                }

                var subject = "Potvrda email adrese za eZkvi";

                var body = $@"
                            Poštovani,

                            Za završetak registracije potvrdite svoju email adresu putem ovog linka:

                            {confirmationUrl}

                            {(user.IsApproved ? "Nakon potvrde emaila možete se prijaviti." : "Nakon potvrde emaila, vaš nalog će biti poslan administratoru na odobrenje.")}";

                await _emailService.SendEmailAsync(user.Email, subject, body);

                TempData["Message"] = user.IsApproved ? "Registracija je uspješna. Potvrdite email adresu pa se prijavite." : "Registracija je uspješna. Provjerite email i potvrdite adresu. Nakon potvrde nalog ide administratoru na odobrenje.";
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                await _userManager.DeleteAsync(user);

                Console.WriteLine("EMAIL VERIFICATION ERROR: " + ex.ToString());

                ModelState.AddModelError("", "Registracija nije završena jer verifikacijski email nije poslan. Pokušajte ponovo kasnije.");
                return View("Login", model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                TempData["Error"] = "Verifikacijski link nije ispravan.";
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                TempData["Error"] = "Korisnik nije pronađen.";
                return RedirectToAction("Login");
            }

            if (user.EmailConfirmed)
            {
                TempData["Message"] = user.IsApproved ? "Email adresa je već potvrđena. Možete se prijaviti." : "Email adresa je već potvrđena. Nalog čeka odobrenje administratora.";
                return RedirectToAction("Login");
            }

            var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

            if (!result.Succeeded)
            {
                TempData["Error"] = "Email nije potvrđen. Link nije ispravan ili je istekao.";
                return RedirectToAction("Login");
            }

            TempData["Message"] = user.IsApproved ? "Email adresa je potvrđena. Možete se prijaviti." : "Email adresa je potvrđena. Nalog sada čeka odobrenje administratora.";
            return RedirectToAction("Login");
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        // GET: /Account/NijeUlogovan
        [HttpGet]
        public IActionResult NijeUlogovan()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // GET: /Account/AccessDenied
        [HttpGet]
        public IActionResult AccessDenied()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction(nameof(NijeUlogovan));
            }

            return View();
        }
    }
}