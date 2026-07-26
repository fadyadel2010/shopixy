import { create } from "zustand";

type SearchAutocompleteState = {
  isOpen: boolean;
  selectedIndex: number;
  open: () => void;
  close: () => void;
  setSelectedIndex: (index: number) => void;
  resetSelection: () => void;
};

export const useSearchAutocompleteStore = create<SearchAutocompleteState>((set) => ({
  isOpen: false,
  selectedIndex: -1,
  open: () =>
    set((state) =>
      state.isOpen
        ? state
        : {
            ...state,
            isOpen: true
          }
    ),
  close: () =>
    set((state) =>
      !state.isOpen && state.selectedIndex === -1
        ? state
        : {
            ...state,
            isOpen: false,
            selectedIndex: -1
          }
    ),
  setSelectedIndex: (selectedIndex) =>
    set((state) =>
      state.selectedIndex === selectedIndex
        ? state
        : {
            ...state,
            selectedIndex
          }
    ),
  resetSelection: () =>
    set((state) =>
      state.selectedIndex === -1
        ? state
        : {
            ...state,
            selectedIndex: -1
          }
    )
}));
