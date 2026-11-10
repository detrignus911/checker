import re

with open('Program.cs', 'r') as f:
    content = f.read()

pattern = r'\{ "([^"]+)", \([^)]+\) \},'
matches = re.finditer(pattern, content)

domains = {}

for match in matches:
    domain = match.group(1)
    # Найдем номер строки
    line_num = content[:match.start()].count('\n') + 1
    
    if domain not in domains:
        domains[domain] = []
    
    domains[domain].append(line_num)

print("Найденные дубликаты:")
for domain, lines in domains.items():
    if len(lines) > 1:
        print(f"Домен: {domain}, строки: {', '.join(map(str, lines))}")