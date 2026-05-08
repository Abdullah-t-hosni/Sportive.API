import sys

def find_strange_chars(filepath):
    try:
        with open(filepath, 'rb') as f:
            content = f.read()
        
        # Try to decode as UTF-8
        try:
            text = content.decode('utf-8')
        except UnicodeDecodeError:
            print("Not valid UTF-8")
            return

        for i, line in enumerate(text.splitlines()):
            strange = []
            for char in line:
                if ord(char) > 127 and not (0x0600 <= ord(char) <= 0x06FF):
                    strange.append(f"{char} (U+{ord(char):04X})")
            if strange:
                print(f"Line {i+1}: {' '.join(strange)}")
                print(f"Content: {line.strip()}")

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    find_strange_chars(sys.argv[1])
