using System.Collections.Generic;
using System.ComponentModel;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class GameTests
    {
        [Fact]
        public void ImageUrl_Setter_InvalidatesCacheAndRaisesPropertyChanged()
        {
            var game = new Game();
            var changedProperties = new List<string?>();

            game.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            game.ImageUrl = "covers/test.png";
            var resolvedImage = game.ImageUrl;

            game.ImageUrl = "covers/test-2.png";

            Assert.Contains(nameof(Game.ImageUrl), changedProperties);
            Assert.EndsWith("test-2.png", game.ImageUrl);
            Assert.NotEqual(resolvedImage, game.ImageUrl);
        }

        [Fact]
        public void ImageUrl_Setter_DoesNotRaisePropertyChanged_WhenValueIsUnchanged()
        {
            var game = new Game();
            var raised = false;

            game.ImageUrl = "covers/test.png";
            _ = game.ImageUrl;

            game.PropertyChanged += OnPropertyChanged;

            game.ImageUrl = "covers/test.png";

            Assert.False(raised);
            return;

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(Game.ImageUrl))
                {
                    raised = true;
                }
            }
        }
    }
}
