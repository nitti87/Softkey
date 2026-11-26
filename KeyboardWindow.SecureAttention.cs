using System;

partial class KeyboardWindow
{
    Func<bool, SecureAttentionResult> secureAttentionInput = SecureAttention.Request;

    void RequestSecureAttention()
    {
        if (desktopSessionSuspended || startupWin != null || !inputDesktopActive()) return;
        CloseMenu(); ClearWordBuffer();
        if (!CancelMouseAction() || !ReleaseHeldModifiers()) return;
        var result = secureAttentionInput(secureMode);
        ShowStatusMessage(result switch
        {
            SecureAttentionResult.Requested => null,
            SecureAttentionResult.MissingUIAccess => "Ctrl + Alt + Delete kräver den installerade hjälpmedelsversionen av SoftKey.",
            SecureAttentionResult.UacDisabled => "Windows användarkontokontroll måste vara på för Ctrl + Alt + Delete.",
            SecureAttentionResult.PolicyBlocked => "Windows-inställningen för Ctrl + Alt + Delete tillåter inte hjälpmedelsappar.",
            SecureAttentionResult.InactiveDesktop or SecureAttentionResult.WrongSession => "Ctrl + Alt + Delete kan inte skickas från den här skärmen just nu.",
            _ => "Windows kunde inte ta emot begäran om Ctrl + Alt + Delete."
        });
        SuppressCurrentDwell();
    }
}
