using CommunityToolkit.Mvvm.Messaging.Messages;

namespace NightInjection.UI.Messages;

public sealed class NavigationRequestMessage(string value) : ValueChangedMessage<string>(value);
