function improveTextareaForCoding(textarea) {
  function indentDelta(text) {
    var parts = text.split(/("(?:\\.|[^"])*"|'(?:\\.|[^'])*'|\/\/.*|[{}()\[\]])/g);
    var delta = 0;

    for (var i = 1; i < parts.length; i += 2) {
      var part = parts[i];
      if ('{(['.indexOf(part) >= 0) {
        delta++;
      } else if ('})]'.indexOf(part) >= 0) {
        delta--;
      }
    }

    return delta;
  }

  textarea.onkeydown = function(e) {
    function lineIndex(index) {
      for (var i = 0; i < lines.length; i++) {
        var line = lines[i];
        if (index <= line.length) {
          return i;
        }
        index -= line.length + 1;
      }
      return lines.length - 1;
    }

    function stripLeadingWhitespace(text) {
      return /^\s*(.*)$/.exec(text)[1];
    }

    if (e.which === 9 || e.which === 13) {
      var lower = Math.min(input.selectionStart, input.selectionEnd);
      var upper = Math.max(input.selectionStart, input.selectionEnd);
      var lines = input.value.split('\n');
      var lowerLine = lineIndex(lower);
      var upperLine = lineIndex(upper);
      var offsets = [];
      var offset = 0;

      for (var i = 0; i < lines.length; i++) {
        offsets.push(offset);
        offset += lines[i].length + 1;
      }

      var lowerColumn = lower - offsets[lowerLine];
      var upperColumn = upper - offsets[upperLine];

      offset = offsets[lowerLine];

      if (e.which === 9) {
        e.preventDefault();

        for (var i = lowerLine; i <= upperLine; i++) {
          var line = lines[i];
          var match = /^(\s*)(.*)$/.exec(line);
          var before = match[1];
          var after = match[2];
          var changed = e.shiftKey ? before.slice(0, -1) : before + '\t';
          if (lower === upper) {
            lower = upper = offset + changed.length;
          } else {
            var delta = changed.length - before.length;
            if (lowerLine === i) {
              lower = offset + lowerColumn + delta;
            }
            if (upperLine === i) {
              upper = offset + upperColumn + delta;
            }
          }
          line = changed + after;
          lines[i] = line;
          offset += line.length + 1;
        }
      }

      else if (e.which === 13) {
        e.preventDefault();

        var skipToEnd = lowerLine === upperLine && (e.metaKey || e.ctrlKey);
        var before = lines.slice(0, lowerLine);
        var after = lines.slice(upperLine + 1);

        if (skipToEnd) {
          lowerColumn = lines[lowerLine].length;
          upperColumn = lines[upperLine].length;
        }

        var indent = /^\s*/.exec(lines[lowerLine])[0];
        if (lowerColumn < indent.length) {
          lowerColumn = indent.length;
        }
        if (upperLine === lowerLine && upperColumn < indent.length) {
          upperColumn = indent.length;
        }

        var lineBefore = lines[lowerLine].slice(0, lowerColumn);
        var lineAfter = stripLeadingWhitespace(lines[upperLine].slice(upperColumn));

        if (e.shiftKey && skipToEnd) {
          if (indentDelta(stripLeadingWhitespace(lineBefore).slice(0, 1)) < 0) {
            indent += '\t';
          }
          var current = [indent, lineBefore];
          lower = upper = offsets[lowerLine] + indent.length;
        }

        else {
          if (indentDelta(lineBefore) > 0) {
            indent += '\t';
          }
          if (indentDelta(lineAfter.slice(0, 1)) < 0) {
            indent = indent.slice(0, -1);
          }
          var current = [lineBefore, indent + lineAfter];
          lower = upper = offsets[lowerLine] + lineBefore.length + 1 + indent.length;
        }

        lines = before.concat(current, after);
      }

      input.value = lines.join('\n');
      input.selectionStart = lower;
      input.selectionEnd = upper;

      if (input.oninput) {
        input.oninput();
      }
    }
  };
}
