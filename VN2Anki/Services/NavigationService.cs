using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public partial class NavigationService : ObservableObject, INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Stack<ObservableObject> _navigationStack = new();

        [ObservableProperty]
        private ObservableObject _currentViewModel;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
        {
            _navigationStack.Clear();
            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            CurrentViewModel = vm;
        }

        public void Push<TViewModel>(Action<TViewModel> configure = null) where TViewModel : ObservableObject
        {
            if (CurrentViewModel != null)
            {
                _navigationStack.Push(CurrentViewModel);
            }
            var vm = _serviceProvider.GetRequiredService<TViewModel>();

            configure?.Invoke(vm);

            CurrentViewModel = vm;
        }

        public void Pop()
        {
            if (_navigationStack.Count > 0)
            {
                CurrentViewModel = _navigationStack.Pop();
            }
        }
    }
}