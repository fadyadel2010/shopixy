import { memo } from "react";

import { SearchAutocomplete } from "./SearchAutocomplete";

type SearchBarProps = {
  onSearchChange?: (value: string) => void;
  placeholder?: string;
};

export const SearchBar = memo(({ onSearchChange, placeholder }: SearchBarProps) => {
  return <SearchAutocomplete onSearchChange={onSearchChange} placeholder={placeholder} />;
});

SearchBar.displayName = "SearchBar";
