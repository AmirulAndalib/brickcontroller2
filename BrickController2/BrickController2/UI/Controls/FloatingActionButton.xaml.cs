﻿using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Graphics;

namespace BrickController2.UI.Controls
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class FloatingActionButton : ContentView
	{
		public FloatingActionButton()
		{
			InitializeComponent();
		}

        public static BindableProperty ButtonColorProperty = BindableProperty.Create(nameof(BackgroundColor), typeof(Color), typeof(FloatingActionButton), default(Color), BindingMode.OneWay, null, ButtonColorChanged);
        public static BindableProperty ImageSourceProperty = BindableProperty.Create(nameof(ImageSource), typeof(ImageSource), typeof(FloatingActionButton), null, BindingMode.OneWay, null, ImageSourceChanged);
        public static BindableProperty ImageColorProperty = BindableProperty.Create(nameof(ImageColor), typeof(Color), typeof(FloatingActionButton), null, BindingMode.OneWay, null, ImageColorChanged);
        public static BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FloatingActionButton), null, BindingMode.OneWay, null, CommandChanged);

        public Color ButtonColor
        {
            get => (Color)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public Color ImageColor
        {
            get => (Color)GetValue(ImageColorProperty);
            set => SetValue(ImageColorProperty, value);
        }

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        private static void ButtonColorChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is FloatingActionButton fab && newValue is Color backgroundColor)
            {
                fab.MyFrame.BackgroundColor = backgroundColor;
            }
        }

        private static void ImageSourceChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is FloatingActionButton fab && newValue is ImageSource imageSource)
            {
                fab.MyImage.Source = imageSource;
            }
        }

        private static void ImageColorChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is FloatingActionButton fab && newValue is Color imageColor)
            {
                fab.MyImage.Color = imageColor;
            }
        }

        private static void CommandChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is FloatingActionButton fab && newValue is ICommand command)
            {
                fab.MyTapGuestureRecognizer.Command = command;
            }
        }
    }
}