using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace VN2Anki.Extensions
{
    public static class ObservableCollectionExtensions
    {
        /// <summary>
        /// Clears the collection and adds all items from the specified collection, 
        /// ensuring the operation is performed on the UI thread.
        /// </summary>
        public static void UpdateFromUIThread<T>(this ObservableCollection<T> collection, IEnumerable<T> newItems)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (newItems == null) throw new ArgumentNullException(nameof(newItems));

            Application.Current.Dispatcher.Invoke(() =>
            {
                collection.Clear();
                foreach (var item in newItems)
                {
                    collection.Add(item);
                }
            });
        }

        /// <summary>
        /// Removes a specific item from the collection on the UI thread.
        /// </summary>
        public static void RemoveFromUIThread<T>(this ObservableCollection<T> collection, T itemToRemove)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (itemToRemove == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                collection.Remove(itemToRemove);
            });
        }
    }
}
